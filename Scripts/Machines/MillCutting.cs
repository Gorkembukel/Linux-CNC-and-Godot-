using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer;

/// <summary>Cutting on a procedural mill: moves the body with the predicted
/// position and cuts whatever stock is mounted with confirmed ones. With no
/// stock mounted the tool cuts nothing.</summary>
public sealed class MillCutting : CuttingProcess
{
    private readonly ProceduralMillBody _body;
    private readonly MachineKinematics _kinematics;

    private VoxelStockVisual _stock;
    private Transform3D _workToStock;
    private Transform3D? _lastPose;

    public double RemovedMm3 { get; private set; }
    public bool HasStock => _stock != null;

    /// <summary>How far the edge finder has gone into the stock: negative =
    /// clear of it, 0 = just touching, positive = pushed in (mm).</summary>
    public float EdgeFinderOvertravelMm { get; private set; } = -1f;

    public MillCutting(MachineSpec spec, BridgeClient bridge, ProceduralMillBody body, ChipSpawner chips) : base(spec, bridge, chips)
    {
        _body = body;
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
        ForgetLooseCuts();
    }

    public void ClearStock()
    {
        _stock = null;
        _lastPose = null;
    }

    protected override void Cut(MachineState s, float seconds)
    {
        if (_stock == null)
            return;
        var inStock = _workToStock * _kinematics.ToolInWork(s.Position);
        var mounted = Tools.Active(s.ToolNumber);
        var tool = mounted.Spec;
        // A tool sticking out further than nominal has its tip that much
        // further from the spindle than the control thinks.
        var pose = new Transform3D(inStock.Basis, inStock.Origin / WorldScale.MmToM - inStock.Basis.Y.Normalized() * mounted.ErrorMm.Y);

        SetCollision(mounted.Present ? BodyInStock(pose, tool.LengthMm + mounted.ErrorMm.Y) : null);

        if (tool?.Kind == ToolKind.EdgeFinder)
        {
            // A ring around the tip, 2 mm up: the deepest point of it inside the stock.
            float radius = tool.DiameterMm / 2f, deepest = -1f;
            for (int i = 0; i < 16; i++)
            {
                float angle = Mathf.Tau * i / 16f;
                var local = new Vector3(Mathf.Cos(angle) * radius, 2f, Mathf.Sin(angle) * radius);
                deepest = Mathf.Max(deepest, _stock.Stock.SampleDistance(pose * local));
            }
            EdgeFinderOvertravelMm = deepest;
            _lastPose = pose;
            return;
        }
        EdgeFinderOvertravelMm = -1f;

        // A broken tool cuts nothing; a worn one cuts undersize.
        if (_lastPose is Transform3D previous && mounted.Cuts)
        {
            var cutter = tool.Cutter();
            cutter = cutter with { RadiusMm = cutter.RadiusMm - mounted.State.EdgeLossMm / 3f };
            var result = _stock.Stock.Cut(previous, pose, cutter);
            if (result.MaterialRemoved)
                _stock.MarkDirty(result);

            float travel = previous.Origin.DistanceTo(pose.Origin);
            if (result.VolumeMm3 > 0f && travel > 1e-4f)
            {
                RemovedMm3 += result.VolumeMm3;
                float rpm = (float)s.SpindleRpm;
                float tipSpeed = new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z).Length();
                var sample = new CutSample(s.ToolNumber, result.VolumeMm3, seconds, rpm, tipSpeed,
                    DepthMm: Mathf.Min(cutter.LengthMm, result.VolumeMm3 / (cutter.RadiusMm * 2f * travel)),
                    FeedPerRevMm: Mathf.Abs(rpm) > 1f ? tipSpeed * 60f / Mathf.Abs(rpm) : 0f,
                    SurfaceSpeedMmPerSec: 2f * Mathf.Pi * cutter.RadiusMm * Mathf.Abs(rpm) / 60f, s.Rapid);
                if (!Report(sample))
                    return;
            }
        }
        _lastPose = pose;
    }

    private const float HolderRadiusMm = 26f;

    /// <summary>Why the tool holder or the spindle nose is inside the stock,
    /// or null. Pose: tool tip in the stock's frame (mm), Y along the tool.</summary>
    private string BodyInStock(Transform3D pose, float toolLengthMm)
    {
        float noseRadius = Spec.Mill.SpindleHousingDiameterMm * 0.375f;
        float noseHeight = toolLengthMm + ToolMeshBuilder.HolderLength / WorldScale.MmToM;
        for (int i = 0; i < 12; i++)
        {
            float angle = Mathf.Tau * i / 12f;
            var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (_stock.Stock.SampleDistance(pose * (around * HolderRadiusMm + Vector3.Up * (toolLengthMm + 1f))) > 0.3f)
                return "ÇARPMA: takım tutucu parçaya çarptı (takımın boyu bu derinliğe yetmiyor)";
            if (_stock.Stock.SampleDistance(pose * (around * noseRadius + Vector3.Up * (noseHeight + 1f))) > 0.3f)
                return "ÇARPMA: iş mili gövdesi parçaya çarptı";
        }
        return null;
    }

    /// <summary>Gap between the tool tip and the stock straight below it
    /// (mm), or null when there is nothing within 2 mm.</summary>
    public float? TipGapMm()
    {
        if (_stock == null || _lastPose is not Transform3D pose)
            return null;
        var axis = pose.Basis.Y.Normalized();
        for (float gap = 0f; gap <= 2f; gap += 0.01f)
            if (_stock.Stock.SampleDistance(pose.Origin - axis * gap) >= 0f)
                return gap;
        return null;
    }

    protected override Vector3 MoveBody(double delta)
    {
        var position = Bridge.Prediction.VisualPosition;
        _body.SetPose(position);
        var mounted = Tools.Active(Bridge.State.ToolNumber);
        _body.ShowTool(mounted.Cuts ? mounted.Spec : null, mounted.ErrorMm.Y);
        _stock?.RebuildDirtyChunks();
        return _kinematics.ToolTransform(position).Origin;
    }
}
