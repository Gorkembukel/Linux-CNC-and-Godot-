using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>Runtime behaviour of a procedural lathe; same flow as
/// LatheMachine (confirmed positions cut the radius profile, a parting cut
/// drops a pickupable part).</summary>
public sealed class LatheCutting
{
    private readonly MachineSpec _spec;
    private readonly BridgeClient _bridge;
    private readonly ProceduralLatheBody _body;
    private readonly WorkpieceVisual _workpiece;
    private readonly ChipSpawner _chips;
    private readonly Node3D _parts;

    private Vector2? _lastPosition;
    private ChipFormation? _chipFormation;
    private double _timeSinceLastCut = 999.0;

    public LatheCutting(MachineSpec spec, BridgeClient bridge, ProceduralLatheBody body, WorkpieceVisual workpiece,
        ChipSpawner chips, Node3D parts)
    {
        _spec = spec;
        _bridge = bridge;
        _body = body;
        _workpiece = workpiece;
        _chips = chips;
        _parts = parts;
    }

    private static Vector2 RadiusAndZ(AxisVector v) => new(v.X, v.Z);

    public void OnConfirmedState()
    {
        var s = _bridge.State;
        var position = RadiusAndZ(s.Position);
        var profile = _workpiece.Profile;

        if (_lastPosition is Vector2 previous)
        {
            float feedPerRev = s.SpindleRpm > 1.0 ? (float)(s.FeedRate / s.SpindleRpm) : 0f;
            var result = profile.Sweep(previous, position, s.ToolNoseRadius, feedPerRev, (float)s.SpindleRpm);
            if (result.MaterialRemoved)
            {
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);
                float surfaceSpeed = 2f * Mathf.Pi * Mathf.Abs(position.X) * (float)s.SpindleRpm / 60f;
                _chipFormation = ChipClassifier.Classify(
                    MaterialPreset.Get(_spec.StockMaterial), result.DepthOfCutMm, feedPerRev, surfaceSpeed);
                _timeSinceLastCut = 0.0;

                if (result.SeveredIndex is int severedIndex)
                    ExtractPart(profile, severedIndex);
            }
        }
        _lastPosition = position;
    }

    private void ExtractPart(LatheRadiusProfile profile, int severedIndex)
    {
        var removedRadii = profile.Sever(severedIndex);
        _workpiece.MarkDirty(severedIndex, profile.BucketCount - 1);

        var built = WorkpiecePartBuilder.Build(removedRadii, profile.StepMm, profile.ZAt(severedIndex + 1),
            MaterialPreset.Get(_spec.StockMaterial).DensityKgPerM3);
        if (built is not WorkpiecePartBuilder.BuiltPart part)
            return;

        var body = new RigidBody3D { Mass = Mathf.Max(0.01f, part.MassKg) };
        _parts.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = part.Mesh, MaterialOverride = _workpiece.StockVisualMaterial });
        body.AddChild(new CollisionShape3D { Shape = part.Collision });
        body.Position = new Vector3(part.CentroidZmm * WorldScale.MmToM, 0f, 0f);
        body.AddToGroup("pickupable");
    }

    public void Process(double delta)
    {
        var position = RadiusAndZ(_bridge.Prediction.VisualPosition);
        _body.SetPose(position.Y, position.X, _bridge.State.ToolNumber, delta);

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2)
            _chipFormation = null;

        _workpiece.RebuildDirtyChunks();
        _chips.Tick(delta, _chipFormation, _body.TipLocal(position.Y, position.X));
    }
}
