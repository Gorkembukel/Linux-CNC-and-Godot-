using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer;

/// <summary>Cutting on a procedural lathe: confirmed positions cut the
/// radius profile, a parting cut drops a pickupable part. With no stock in
/// the chuck the tool cuts nothing.</summary>
public sealed class LatheCutting : CuttingProcess
{
    private readonly ProceduralLatheBody _body;
    private readonly WorkpieceVisual _workpiece;
    private readonly Node3D _parts;

    private Vector2? _lastPosition;
    /// <summary>Where the current threading pass started: every pass of a
    /// thread starts at the same Z, which keeps them in phase.</summary>
    private float? _threadAnchorZ;
    private float _threadTravel, _threadRevs, _threadPitch;

    /// <summary>Material has been removed since the last ResetStock.</summary>
    public bool HasCut { get; private set; }

    /// <summary>Raised after a parting cut has dropped a piece.</summary>
    public System.Action Severed;

    public LatheCutting(MachineSpec spec, BridgeClient bridge, ProceduralLatheBody body, WorkpieceVisual workpiece,
        ChipSpawner chips, Node3D parts) : base(spec, bridge, chips)
    {
        _body = body;
        _workpiece = workpiece;
        _parts = parts;
    }

    /// <summary>Real tip position (radius, Z) for a machine position.</summary>
    public Vector2 TipAt(AxisVector v, int toolNumber)
    {
        v = Errors.Apply(v);
        var mounted = Tools.Active(toolNumber);
        return mounted.Present
            ? new Vector2(v.X + mounted.State.EdgeLossMm + mounted.ErrorMm.X, v.Z + mounted.ErrorMm.Z)
            : new Vector2(v.X, v.Z);
    }

    /// <summary>A different bar is in the chuck now.</summary>
    public void ResetStock()
    {
        _lastPosition = null;
        HasCut = false;
        ForgetLooseCuts();
    }

    protected override void Cut(MachineState s, float seconds)
    {
        var position = TipAt(s.Position, s.ToolNumber);
        var profile = _workpiece.Profile;
        var mounted = Tools.Active(s.ToolNumber);
        string machineHit = s.MachineOn ? _body.Interference((float)s.Position.Z, (float)s.Position.X) : null;
        if (profile == null)
        {
            SetCollision(machineHit);
            _lastPosition = null;
            return;
        }

        SetCollision(machineHit ?? CarriageInStock(profile, (float)s.Position.Z, (float)s.Position.X)
            ?? (mounted.Present ? BodyInStock(profile, position, mounted.Spec) : null));

        if (_lastPosition is Vector2 previous && mounted.Cuts)
        {
            float rpm = (float)System.Math.Abs(s.SpindleRpm);
            // From the tip's own travel: the bridge's feed_rate field is the
            // override scale, not a feed.
            float tipSpeed = previous.DistanceTo(position) / Mathf.Max(seconds, 1e-3f);
            float feedPerRev = rpm > 1f ? tipSpeed * 60f / rpm : 0f;
            var kind = mounted.Spec.Kind;
            float drillRadius = kind == ToolKind.Drill ? mounted.Spec.DiameterMm / 2f : 0f;
            var shape = mounted.Spec.LatheProfile;
            float alongZ = Mathf.Abs(position.Y - previous.Y), acrossX = Mathf.Abs(position.X - previous.X);
            bool inThread = _threadAnchorZ != null;
            // Once a pass is on, it stays one while it slows down at the end.
            bool threading = shape != null && mounted.Spec.Geometry.IsThreading && rpm > 1f && alongZ > acrossX
                             && (inThread || feedPerRev > 0.2f);
            // Backing out of the groove at the end of a pass: the tool sits in
            // its own thread (the form is stamped every pitch from the anchor,
            // not where the tool stopped), so it keeps the pass's phase.
            bool leavingGroove = inThread && !threading && acrossX >= alongZ && Mathf.Abs(position.X) >= Mathf.Abs(previous.X);
            if (!threading && !leavingGroove)
                _threadAnchorZ = null;
            else if (_threadAnchorZ == null)
            {
                _threadAnchorZ = previous.Y;
                _threadTravel = _threadRevs = 0f;
            }
            float pitch = 0f;
            if (threading)
            {
                // Sample-to-sample feed is noisy; over a pass the axis follows the spindle exactly.
                _threadTravel += alongZ;
                _threadRevs += rpm / 60f * seconds;
                pitch = _threadPitch = Mathf.Snapped(_threadRevs > 0.3f ? _threadTravel / _threadRevs : feedPerRev, 0.05f);
            }
            else if (leavingGroove)
                pitch = _threadPitch;
            var result = kind == ToolKind.Drill ? profile.SweepBore(previous, position, s.ToolNoseRadius, drillRadius, mounted.Spec.TipAngleDeg)
                : shape != null && kind == ToolKind.Boring ? profile.SweepProfileBore(previous, position, shape)
                : shape != null ? profile.SweepProfile(previous, position, shape, pitch, _threadAnchorZ ?? 0f)
                : kind == ToolKind.Boring ? profile.SweepBore(previous, position, s.ToolNoseRadius, 0f)
                : profile.Sweep(previous, position, s.ToolNoseRadius, feedPerRev, rpm);
            if (result.MaterialRemoved)
            {
                HasCut = true;
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);
                float cuttingRadius = drillRadius > 0f ? drillRadius : Mathf.Abs(position.X);
                var sample = new CutSample(s.ToolNumber, result.VolumeMm3, seconds, rpm, tipSpeed,
                    result.DepthOfCutMm, feedPerRev, SurfaceSpeedMmPerSec: 2f * Mathf.Pi * cuttingRadius * rpm / 60f, s.Rapid);
                if (!Report(sample))
                    return;

                if (result.SeveredIndex is int severedIndex)
                {
                    ExtractPart(profile, severedIndex);
                    Severed?.Invoke();
                }
            }
        }
        _lastPosition = position;
    }

    /// <summary>Why the tool's body (not its cutting edge) is inside the
    /// bar, or null. An insert reaches 9 mm below its shank, a parting blade
    /// 30 mm. A turning tool's shank trails the tip on the tailstock side,
    /// beyond the insert itself (which is what faces off the bar's end).</summary>
    private static string BodyInStock(LatheRadiusProfile profile, Vector2 tip, ToolSpec tool)
    {
        if (tool.Kind == ToolKind.Drill)
            return DrillHolderInStock(profile, tip, tool);
        if (tool.Kind == ToolKind.Boring)
            return BoringBarInStock(profile, tip, tool);
        bool parting = tool.Kind == ToolKind.Parting;
        var shape = tool.LatheProfile;
        float reach = parting ? (tool.Geometry?.DepthMm ?? 30f) : shape != null ? shape.Height : LatheRadiusProfile.PlainInsertHeightMm;
        float from = parting ? tip.Y - 6f : shape != null ? tip.Y + profile.StepMm / 2f : tip.Y + 6f;
        float to = parting ? tip.Y + 6f : shape != null ? tip.Y + Mathf.Max(shape.ZMax, 12f) : tip.Y + 18f;
        for (float z = Mathf.Max(from, profile.ZMin); z <= Mathf.Min(to, profile.ZMax); z += profile.StepMm)
            if (profile.RadiusAt(profile.IndexAt(z)) > tip.X + reach + 0.3f)
                return parting
                    ? "ÇARPMA: kesme kaleminin gövdesi parçaya çarptı (bıçak boyundan derin kanal)"
                    : "ÇARPMA: kalemin gövdesi parçaya çarptı (uç bu kadar derine inemez)";
        return null;
    }

    /// <summary>Deeper than the drill's flutes: the holder meets the face.</summary>
    private static string DrillHolderInStock(LatheRadiusProfile profile, Vector2 tip, ToolSpec tool)
    {
        float holderZ = tip.Y + tool.LengthMm;
        if (holderZ < profile.ZMin || holderZ > profile.ZMax)
            return null;
        int index = profile.IndexAt(holderZ);
        float reach = Mathf.Abs(tip.X) + tool.DiameterMm / 2f + 5f;
        return profile.RadiusAt(index) > profile.InnerAt(index) + 0.3f && profile.InnerAt(index) < reach
            ? "ÇARPMA: matkap tutucusu parçaya çarptı (matkap boyu bu derinliğe yetmiyor)"
            : null;
    }

    /// <summary>A boring bar lies on the centreline side of its edge, from
    /// the tip out of the bore: wherever that line is in solid material, the
    /// bar is inside the part (hole too small for the bar, or no hole yet).</summary>
    private static string BoringBarInStock(LatheRadiusProfile profile, Vector2 tip, ToolSpec tool)
    {
        float edge = tip.X - tool.TipRadiusMm, back = edge - tool.DiameterMm;
        float near = back <= 0f && edge >= 0f ? 0f : Mathf.Min(Mathf.Abs(back), Mathf.Abs(edge));
        float far = Mathf.Max(Mathf.Abs(back), Mathf.Abs(edge));
        float from = Mathf.Max(tip.Y + 2f, profile.ZMin), to = Mathf.Min(tip.Y + tool.LengthMm, profile.ZMax);
        for (float z = from; z <= to; z += profile.StepMm)
        {
            int i = profile.IndexAt(z);
            if (profile.RadiusAt(i) > 0.05f && far > profile.InnerAt(i) + 0.3f && near < profile.RadiusAt(i) - 0.3f)
                return "ÇARPMA: bor barı parçaya çarptı (delik bar için dar ya da önce delinmemiş)";
        }
        return null;
    }

    /// <summary>The turret or tool post itself (not the tool) inside the bar.</summary>
    private string CarriageInStock(LatheRadiusProfile profile, float zMm, float xMm)
    {
        foreach (var part in _body.Carried(zMm, xMm))
        {
            for (float z = Mathf.Max(part.ZMin, profile.ZMin); z <= Mathf.Min(part.ZMax, profile.ZMax); z += profile.StepMm)
                if (profile.RadiusAt(profile.IndexAt(z)) > part.Reach)
                    return $"ÇARPMA: {part.What} parçaya çarptı (takım tarete göre çok kısa bağlı)";
        }
        return null;
    }

    /// <summary>A hand saw has gone through the bar at this Z: the piece
    /// beyond the kerf drops, the kerf goes with the chips.</summary>
    public void SawThrough(float zMm, float kerfMm)
    {
        var profile = _workpiece.Profile;
        int i = profile.IndexAt(zMm), kerf = Mathf.Max(1, Mathf.RoundToInt(kerfMm / profile.StepMm));
        ExtractPart(profile, Mathf.Min(i + kerf, profile.BucketCount - 1));
        profile.Sever(i);
        _workpiece.MarkDirty(i, profile.BucketCount - 1);
        HasCut = true;
        Severed?.Invoke();
    }

    private void ExtractPart(LatheRadiusProfile profile, int severedIndex)
    {
        var removedRadii = profile.Sever(severedIndex, out var removedInner);
        _workpiece.MarkDirty(severedIndex, profile.BucketCount - 1);

        var part = StockItem.CreateTurned(removedRadii, removedInner, profile.StepMm, Material, profile.SquareHalfMm);
        if (part == null)
            return;
        part.DisplayName += SurfaceNote();
        // Parted or sawn off: the cut face keeps a burr (the parting pip).
        part.Burred = true;
        PartFinished?.Invoke();
        _parts.AddChild(part);
        float middle = profile.ZAt(severedIndex + 1) + part.Blank.LengthMm / 2f;
        part.Position = new Vector3(middle * WorldScale.MmToM, 0f, 0f);
    }

    protected override Vector3 MoveBody(double delta)
    {
        int tool = Bridge.State.ToolNumber;
        var position = TipAt(Bridge.Prediction.VisualPosition, tool);
        _body.SetPose(position.Y, position.X, tool, delta);
        _body.SetCoolant(Bridge.State.Coolant && Spec.FloodCoolant);
        _workpiece.RebuildDirtyChunks();
        return _body.TipLocal(position.Y, position.X);
    }
}
