using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Machining;
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
    private readonly ToolLengthModel _lengths;

    private VoxelStockVisual _stock;
    private Transform3D _workToStock;
    private Transform3D? _lastPose;

    public double RemovedMm3 { get; private set; }
    public bool HasStock => _stock != null;

    /// <summary>How far the measuring tool in the spindle (edge finder ring,
    /// touch probe ball) has gone into the stock: negative = clear of it,
    /// 0 = just touching, positive = pushed in (mm).</summary>
    public float MeasuringOvertravelMm { get; private set; } = -1f;

    public MillCutting(MachineSpec spec, BridgeClient bridge, ProceduralMillBody body, ChipSpawner chips) : base(spec, bridge, chips)
    {
        _body = body;
        _kinematics = new MachineKinematics(spec.AxisDefs());
        _lengths = spec.ToolLengths();
    }

    /// <summary>How far this tool's real tip sits below the kinematic
    /// reference point, along the tool axis (mm).</summary>
    public float TipBelowReferenceMm(MountedTool mounted) =>
        mounted.Present ? _lengths.TipBelowReferenceMm(mounted.Spec.LengthMm, mounted.ErrorMm.Y) : 0f;

    /// <summary>Real tip pose of this tool relative to the part of the
    /// machine that carries the workpiece (metres); basis Y points from the
    /// tip toward the spindle.</summary>
    public Transform3D TipInWork(AxisVector position, MountedTool mounted) =>
        DownTool(_kinematics.ToolInWork(position), TipBelowReferenceMm(mounted) * WorldScale.MmToM);

    private static Transform3D DownTool(Transform3D pose, float distance) =>
        new(pose.Basis, pose.Origin - pose.Basis.Y.Normalized() * distance);

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
        MeasuringOvertravelMm = -1f;
        _lastPose = null;
    }

    protected override void Cut(MachineState s, float seconds)
    {
        if (_stock == null)
            return;
        var inStock = _workToStock * _kinematics.ToolInWork(Errors.Apply(s.Position));
        var mounted = Tools.Active(s.ToolNumber);
        var tool = mounted.Spec;
        var pose = DownTool(new Transform3D(inStock.Basis, inStock.Origin / WorldScale.MmToM), TipBelowReferenceMm(mounted));

        SetCollision(mounted.Present ? BodyInStock(pose, tool.LengthMm + mounted.ErrorMm.Y) : null);

        if (tool?.Kind == ToolKind.TouchProbe)
        {
            // Points on the ball, also where they will be after the signal's
            // trip to the control (ProbeSignalLeadSec), so G38 stops on the
            // surface. The deepest of them is how far the stylus has given.
            float ball = tool.DiameterMm / 2f;
            var centre = pose * new Vector3(0f, ball, 0f);
            var lead = _lastPose is Transform3D last && seconds > 1e-4f
                ? (pose.Origin - last.Origin) * (Spec.Mill.ProbeSignalLeadSec / seconds)
                : Vector3.Zero;
            float deepest = -1f;
            foreach (var direction in BallDirections)
            {
                var point = centre + direction * ball;
                deepest = Mathf.Max(deepest, Mathf.Max(_stock.Stock.SampleDistance(point), _stock.Stock.SampleDistance(point + lead)));
            }
            MeasuringOvertravelMm = deepest;
            _lastPose = pose;
            return;
        }
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
            MeasuringOvertravelMm = deepest;
            _lastPose = pose;
            return;
        }
        MeasuringOvertravelMm = -1f;

        // A broken tool cuts nothing; a worn one, or one the cut bends away, leaves the wall short.
        if (_lastPose is Transform3D previous && mounted.Cuts)
        {
            var cutter = tool.Cutter();
            cutter = cutter with { RadiusMm = cutter.RadiusMm - mounted.State.EdgeLossMm / 3f - DeflectionMm() };
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

    /// <summary>26 directions round a ball (faces, edges and corners of a
    /// cube), where the probe's ball is sampled.</summary>
    private static readonly Vector3[] BallDirections =
        (from x in new[] { -1, 0, 1 } from y in new[] { -1, 0, 1 } from z in new[] { -1, 0, 1 }
         where x != 0 || y != 0 || z != 0 select new Vector3(x, y, z).Normalized()).ToArray();

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
    public float? TipGapMm(float maxMm = 2f)
    {
        if (_stock == null || _lastPose is not Transform3D pose)
            return null;
        var axis = pose.Basis.Y.Normalized();
        bool Solid(float gap) => _stock.Stock.SampleDistance(pose.Origin - axis * gap) >= 0f;
        // Coarse steps down to the surface, then 0.005 mm back up to it.
        for (float coarse = 0f; coarse <= maxMm + CoarseGapStepMm; coarse += CoarseGapStepMm)
        {
            if (!Solid(coarse))
                continue;
            float gap = coarse;
            while (gap > 0f && Solid(gap - FineGapStepMm))
                gap -= FineGapStepMm;
            return gap <= maxMm ? gap : null;
        }
        return null;
    }

    private const float CoarseGapStepMm = 0.25f, FineGapStepMm = 0.005f;

    protected override Vector3 MoveBody(double delta)
    {
        var position = Errors.Apply(BodyPosition);
        _body.SetPose(position);
        var mounted = Tools.Active(Bridge.State.ToolNumber);
        _body.ShowTool(mounted.Cuts ? mounted.Spec : null, mounted.ErrorMm.Y);
        _body.SetCoolant(Bridge.State.Coolant && Spec.FloodCoolant);
        _stock?.RebuildDirtyChunks();
        return DownTool(_kinematics.ToolTransform(position), TipBelowReferenceMm(mounted) * WorldScale.MmToM).Origin;
    }
}
