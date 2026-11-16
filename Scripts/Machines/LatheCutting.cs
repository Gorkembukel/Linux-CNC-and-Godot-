using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;

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

        if (_lastPosition is Vector2 previous && Tools.Active(s.ToolNumber).Cuts)
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
                    result.DepthOfCutMm, feedPerRev, SurfaceSpeedMmPerSec: 2f * Mathf.Pi * Mathf.Abs(position.X) * rpm / 60f);
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

    private void ExtractPart(LatheRadiusProfile profile, int severedIndex)
    {
        var removedRadii = profile.Sever(severedIndex);
        _workpiece.MarkDirty(severedIndex, profile.BucketCount - 1);

        var built = WorkpiecePartBuilder.Build(removedRadii, profile.StepMm, profile.ZAt(severedIndex + 1),
            MaterialPreset.Get(Material).DensityKgPerM3);
        if (built is not WorkpiecePartBuilder.BuiltPart part)
            return;

        var body = new RigidBody3D { Mass = Mathf.Max(0.01f, part.MassKg) };
        _parts.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = part.Mesh, MaterialOverride = _workpiece.StockVisualMaterial });
        body.AddChild(new CollisionShape3D { Shape = part.Collision });
        body.Position = new Vector3(part.CentroidZmm * WorldScale.MmToM, 0f, 0f);
        body.AddToGroup("pickupable");
    }

    protected override Vector3 MoveBody(double delta)
    {
        int tool = Bridge.State.ToolNumber;
        var position = TipAt(Bridge.Prediction.VisualPosition, tool);
        _body.SetPose(position.Y, position.X, tool, delta);
        _workpiece.RebuildDirtyChunks();
        return _body.TipLocal(position.Y, position.X);
    }
}
