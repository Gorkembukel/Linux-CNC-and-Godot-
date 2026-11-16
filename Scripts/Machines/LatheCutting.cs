using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>Runtime behaviour of a procedural lathe; same flow as
/// LatheMachine (confirmed positions cut the radius profile, a parting cut
/// drops a pickupable part). With no stock in the chuck the tool cuts nothing.</summary>
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
    private int _cutsWhileLoose;

    /// <summary>What the bar in the chuck is made of.</summary>
    public MaterialKind Material;
    /// <summary>Material has been removed since the last ResetStock.</summary>
    public bool HasCut { get; private set; }

    /// <summary>Asked on every cut: is the bar held firmly? Null = always.</summary>
    public System.Func<bool> IsSecure;
    /// <summary>Raised once when a cut is taken on a bar that is not secure.</summary>
    public System.Action CutWhileLoose;
    /// <summary>Raised after a parting cut has dropped a piece.</summary>
    public System.Action Severed;
    /// <summary>Where a tool's tip really is relative to where the control
    /// believes it is: (radius, Z) in mm for a tool number.</summary>
    public System.Func<int, Vector2> TipError;

    public LatheCutting(MachineSpec spec, BridgeClient bridge, ProceduralLatheBody body, WorkpieceVisual workpiece,
        ChipSpawner chips, Node3D parts)
    {
        _spec = spec;
        _bridge = bridge;
        _body = body;
        _workpiece = workpiece;
        _chips = chips;
        _parts = parts;
        Material = spec.StockMaterial;
    }

    /// <summary>Real tip position (radius, Z) for a machine position.</summary>
    public Vector2 TipAt(AxisVector v, int toolNumber) =>
        new Vector2(v.X, v.Z) + (TipError?.Invoke(toolNumber) ?? Vector2.Zero);

    /// <summary>A different bar is in the chuck now.</summary>
    public void ResetStock()
    {
        _lastPosition = null;
        _cutsWhileLoose = 0;
        HasCut = false;
    }

    public void OnConfirmedState()
    {
        var s = _bridge.State;
        var position = TipAt(s.Position, s.ToolNumber);
        var profile = _workpiece.Profile;
        if (profile == null)
        {
            _lastPosition = null;
            return;
        }

        if (_lastPosition is Vector2 previous)
        {
            float feedPerRev = s.SpindleRpm > 1.0 ? (float)(s.FeedRate / s.SpindleRpm) : 0f;
            var result = profile.Sweep(previous, position, s.ToolNoseRadius, feedPerRev, (float)s.SpindleRpm);
            if (result.MaterialRemoved)
            {
                HasCut = true;
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);
                float surfaceSpeed = 2f * Mathf.Pi * Mathf.Abs(position.X) * (float)s.SpindleRpm / 60f;
                _chipFormation = ChipClassifier.Classify(
                    MaterialPreset.Get(Material), result.DepthOfCutMm, feedPerRev, surfaceSpeed);
                _timeSinceLastCut = 0.0;

                // A light touch only rattles the bar; a real cut throws it.
                if (IsSecure != null && !IsSecure() && ++_cutsWhileLoose > 3)
                {
                    _cutsWhileLoose = 0;
                    CutWhileLoose?.Invoke();
                    return;
                }

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

    public void Process(double delta)
    {
        int tool = _bridge.State.ToolNumber;
        var position = TipAt(_bridge.Prediction.VisualPosition, tool);
        _body.SetPose(position.Y, position.X, tool, delta);

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2)
            _chipFormation = null;

        _workpiece.RebuildDirtyChunks();
        _chips.Tick(delta, _chipFormation, _body.TipLocal(position.Y, position.X));
    }
}
