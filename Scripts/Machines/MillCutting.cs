using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>Runtime behaviour of a procedural mill: moves the body with the
/// predicted position and cuts the voxel stock with confirmed ones.</summary>
public sealed class MillCutting
{
    private readonly MachineSpec _spec;
    private readonly BridgeClient _bridge;
    private readonly ProceduralMillBody _body;
    private readonly VoxelStockVisual _stock;
    private readonly ChipSpawner _chips;
    private readonly MachineKinematics _kinematics;

    private Transform3D? _lastPose;
    private ChipFormation? _chipFormation;
    private double _timeSinceLastCut = 999.0;

    public double RemovedMm3 { get; private set; }

    public MillCutting(MachineSpec spec, BridgeClient bridge, ProceduralMillBody body, VoxelStockVisual stock, ChipSpawner chips)
    {
        _spec = spec;
        _bridge = bridge;
        _body = body;
        _stock = stock;
        _chips = chips;
        _kinematics = new MachineKinematics(spec.AxisDefs());
    }

    public void OnConfirmedState()
    {
        var s = _bridge.State;
        var inWork = _kinematics.ToolInWork(s.Position);
        var pose = new Transform3D(inWork.Basis, inWork.Origin / WorldScale.MmToM);
        var tool = _spec.FindTool(s.ToolNumber);

        if (_lastPose is Transform3D previous && tool != null)
        {
            var cutter = tool.Cutter();
            var result = _stock.Stock.Cut(previous, pose, cutter);
            if (result.MaterialRemoved)
                _stock.MarkDirty(result);

            float travel = previous.Origin.DistanceTo(pose.Origin);
            if (result.VolumeMm3 > 0f && travel > 1e-4f)
            {
                RemovedMm3 += result.VolumeMm3;
                float rpm = (float)s.SpindleRpm;
                float tipSpeed = new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z).Length();
                float feedPerRev = rpm > 1f ? tipSpeed * 60f / rpm : 0f;
                float depth = Mathf.Min(cutter.LengthMm, result.VolumeMm3 / (cutter.RadiusMm * 2f * travel));
                float surfaceSpeed = 2f * Mathf.Pi * cutter.RadiusMm * rpm / 60f;
                _chipFormation = ChipClassifier.Classify(MaterialPreset.Get(_spec.StockMaterial), depth, feedPerRev, surfaceSpeed);
                _timeSinceLastCut = 0.0;
            }
        }
        _lastPose = pose;
    }

    public void Process(double delta)
    {
        var position = _bridge.Prediction.VisualPosition;
        _body.SetPose(position);
        _body.SetTool(_bridge.State.ToolNumber);

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2)
            _chipFormation = null;

        _stock.RebuildDirtyChunks();
        _chips.Tick(delta, _chipFormation, _kinematics.ToolTransform(position).Origin);
    }
}
