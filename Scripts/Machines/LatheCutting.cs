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
        if (profile == null)
        {
            _lastPosition = null;
            return;
        }

        var mounted = Tools.Active(s.ToolNumber);
        SetCollision(mounted.Present ? BodyInStock(profile, position, mounted.Spec) : null);

        if (_lastPosition is Vector2 previous && mounted.Cuts)
        {
            float rpm = (float)System.Math.Abs(s.SpindleRpm);
            // From the tip's own travel: the bridge's feed_rate field is the
            // override scale, not a feed.
            float tipSpeed = previous.DistanceTo(position) / Mathf.Max(seconds, 1e-3f);
            float feedPerRev = rpm > 1f ? tipSpeed * 60f / rpm : 0f;
            var result = profile.Sweep(previous, position, s.ToolNoseRadius, feedPerRev, rpm);
            if (result.MaterialRemoved)
            {
                HasCut = true;
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);
                var sample = new CutSample(s.ToolNumber, result.VolumeMm3, seconds, rpm, tipSpeed,
                    result.DepthOfCutMm, feedPerRev, SurfaceSpeedMmPerSec: 2f * Mathf.Pi * Mathf.Abs(position.X) * rpm / 60f, s.Rapid);
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
        if (tool.Kind is ToolKind.Boring or ToolKind.Drill)
            return null;
        bool parting = tool.Kind == ToolKind.Parting;
        float reach = parting ? 30f : 9f;
        float from = parting ? tip.Y - 6f : tip.Y + 6f, to = parting ? tip.Y + 6f : tip.Y + 18f;
        for (float z = Mathf.Max(from, profile.ZMin); z <= Mathf.Min(to, profile.ZMax); z += profile.StepMm)
            if (profile.RadiusAt(profile.IndexAt(z)) > tip.X + reach + 0.3f)
                return parting
                    ? "ÇARPMA: kesme kaleminin gövdesi parçaya çarptı (bıçak boyundan derin kanal)"
                    : "ÇARPMA: kalemin gövdesi parçaya çarptı (uç bu kadar derine inemez)";
        return null;
    }

    private void ExtractPart(LatheRadiusProfile profile, int severedIndex)
    {
        var removedRadii = profile.Sever(severedIndex);
        _workpiece.MarkDirty(severedIndex, profile.BucketCount - 1);

        var part = TurnedPart.Build(removedRadii, profile.StepMm, profile.ZAt(severedIndex + 1),
            MaterialPreset.Get(Material).DensityKgPerM3, _workpiece.StockVisualMaterial, out float centreZ);
        if (part == null)
            return;
        part.DisplayName += SurfaceNote();
        PartFinished?.Invoke();
        _parts.AddChild(part);
        part.Position = new Vector3(centreZ * WorldScale.MmToM, 0f, 0f);
    }

    protected override Vector3 MoveBody(double delta)
    {
        int tool = Bridge.State.ToolNumber;
        var position = TipAt(Bridge.Prediction.VisualPosition, tool);
        _body.SetPose(position.Y, position.X, tool, delta);
        _body.SetCoolant(Bridge.State.Coolant);
        _workpiece.RebuildDirtyChunks();
        return _body.TipLocal(position.Y, position.X);
    }
}
