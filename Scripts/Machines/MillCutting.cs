using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>Runtime behaviour of a procedural mill: moves the body with the
/// predicted position and cuts whatever stock is mounted with confirmed
/// ones. With no stock mounted the tool cuts nothing.</summary>
public sealed class MillCutting
{
    private readonly MachineSpec _spec;
    private readonly BridgeClient _bridge;
    private readonly ProceduralMillBody _body;
    private readonly ChipSpawner _chips;
    private readonly MachineKinematics _kinematics;

    private VoxelStockVisual _stock;
    private Transform3D _workToStock;
    private Transform3D? _lastPose;
    private ChipFormation? _chipFormation;
    private double _timeSinceLastCut = 999.0;
    private float _removedWhileLoose;

    public double RemovedMm3 { get; private set; }
    public bool HasStock => _stock != null;

    /// <summary>Asked on every cut: is the stock held firmly? Null = always.</summary>
    public System.Func<bool> IsSecure;
    /// <summary>Raised once when a cut is taken on stock that is not secure.</summary>
    public System.Action CutWhileLoose;

    public MillCutting(MachineSpec spec, BridgeClient bridge, ProceduralMillBody body, ChipSpawner chips)
    {
        _spec = spec;
        _bridge = bridge;
        _body = body;
        _chips = chips;
        _kinematics = new MachineKinematics(spec.AxisDefs());
    }

    /// <summary>Tool tip position in the body's frame (metres).</summary>
    public Vector3 TipInBody(AxisVector position) => _kinematics.ToolTransform(position).Origin;

    /// <summary>Tool tip pose relative to the part of the machine that
    /// carries the workpiece (metres).</summary>
    public Transform3D ToolInWork(AxisVector position) => _kinematics.ToolInWork(position);

    /// <summary>Starts cutting this stock where it now sits. It must already
    /// be parented under the body's WorkEnd.</summary>
    public void MountStock(VoxelStockVisual stock)
    {
        _stock = stock;
        _workToStock = stock.GlobalTransform.AffineInverse() * _body.WorkEnd.GlobalTransform;
        _lastPose = null;
        _removedWhileLoose = 0f;
    }

    public void ClearStock()
    {
        _stock = null;
        _lastPose = null;
    }

    public void OnConfirmedState()
    {
        if (_stock == null)
            return;
        var s = _bridge.State;
        var inStock = _workToStock * _kinematics.ToolInWork(s.Position);
        var pose = new Transform3D(inStock.Basis, inStock.Origin / WorldScale.MmToM);
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

                if (IsSecure != null && !IsSecure())
                {
                    _removedWhileLoose += result.VolumeMm3;
                    // A light touch only rattles the part; a real cut throws it.
                    if (_removedWhileLoose > 20f)
                    {
                        _removedWhileLoose = 0f;
                        CutWhileLoose?.Invoke();
                        return;
                    }
                }
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

        _stock?.RebuildDirtyChunks();
        _chips.Tick(delta, _chipFormation, _kinematics.ToolTransform(position).Origin);
    }
}
