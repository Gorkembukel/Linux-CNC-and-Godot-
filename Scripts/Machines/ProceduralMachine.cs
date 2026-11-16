using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Fixtures;

namespace CncTrainer;

/// <summary>
/// A machine built entirely from its MachineSpec: body, stock, LinuxCNC
/// configuration and bridge. The scene file only stores this node, the spec
/// and the ConsoleStand child; everything else is rebuilt from the spec, in
/// the editor too, so editing the spec in the Inspector reshapes the machine
/// on the spot. The node's origin is on the floor.
/// </summary>
[Tool]
public partial class ProceduralMachine : Node3D, IMachine
{
    [Export] public MachineSpec Spec { get; set; }

    public MachineRig Rig { get; private set; }
    public Scheduling.JobScheduler Scheduler => null;

    private const int EditorPreviewNodes = 150_000;
    private const int RuntimeNodes = 6_000_000;

    private Node3D _work;
    private ProceduralMillBody _millBody;
    private ProceduralLatheBody _latheBody;
    private VoxelStockVisual _voxelStock;
    private WorkpieceVisual _workpiece;
    private MillCutting _mill;
    private LatheCutting _lathe;
    private string _fingerprint;
    private double _fingerprintTimer;

    public override void _Ready()
    {
        Rebuild();
        if (!Engine.IsEditorHint() && _work != null)
            StartMachine();
    }

    public override string[] _GetConfigurationWarnings() =>
        Spec == null ? new[] { "Assign a MachineSpec." } : Spec.Validate().ToArray();

    private void Rebuild()
    {
        if (_work != null)
        {
            RemoveChild(_work);
            _work.QueueFree();
            _work = null;
        }
        _fingerprint = Spec?.Fingerprint();
        UpdateConfigurationWarnings();
        if (Spec == null || Spec.Validate().Count > 0)
            return;

        _work = new Node3D { Name = "Work", Position = new Vector3(0f, Spec.WorkHeightMm * WorldScale.MmToM, 0f) };
        AddChild(_work);

        if (Spec.Kind == MachineKind.Mill)
        {
            _millBody = new ProceduralMillBody { Name = "Body" };
            _work.AddChild(_millBody);
            _millBody.Build(Spec);
            if (!HandsOnMill)
            {
                _voxelStock = new VoxelStockVisual { Name = "Stock" };
                _millBody.WorkEnd.AddChild(_voxelStock);
                _voxelStock.Build(NewStock(Engine.IsEditorHint() ? EditorPreviewNodes : RuntimeNodes));
            }
        }
        else
        {
            var l = Spec.Lathe;
            _workpiece = new WorkpieceVisual
            {
                Name = "Workpiece",
                StockRadiusMm = l.StockDiameterMm / 2f,
                StockZMin = l.StockZMin,
                StockZMax = l.StockZMax,
                StartEmpty = Spec.ManualSetup,
            };
            _work.AddChild(_workpiece);
            _latheBody = new ProceduralLatheBody { Name = "Body" };
            _work.AddChild(_latheBody);
            _latheBody.Build(Spec);
        }
    }

    private bool HandsOnMill => Spec.ManualSetup && Spec.Mill.Workholding != Workholding.None;

    private VoxelStock NewStock(int maxNodes)
    {
        var m = Spec.Mill;
        float step = VoxelStock.StepFor(new Vector3(m.StockSizeXMm, m.StockHeight, m.StockSizeYMm), m.VoxelStepMm, maxNodes);
        return m.StockShape == StockShape.Block
            ? VoxelStock.Block(m.StockSizeXMm, m.StockHeightMm, m.StockSizeYMm, step)
            : VoxelStock.CylinderAlongX(m.StockSizeXMm, m.StockSizeYMm, step);
    }

    private void StartMachine()
    {
        Spec.WriteConfigFiles();

        // BridgeClient binds its sockets in its own _Ready.
        var bridge = new BridgeClient { Name = "BridgeClient", StatePort = Spec.StatePort, CommandPort = Spec.CommandPort };
        AddChild(bridge);

        var axes = AxisDef.InJointOrder(Spec.AxisDefs()).Select((a, joint) => (a.Letter, (byte)joint)).ToArray();
        var profile = new MachineProfile(Spec.MachineName, axes, Spec.WslDistro, Spec.StatePort, Spec.CommandPort,
            Spec.RateHz, Spec.ConfigStem + ".ini", Spec.DemoProgram);
        Rig = new MachineRig(profile, bridge);
        MachineProcessController.StartBackingProcess(profile);
        GetNodeOrNull<ConsoleStand>("ConsoleStand")?.Configure(Rig);

        var chips = new ChipSpawner { Name = "ChipSpawner" };
        if (Spec.Kind == MachineKind.Mill)
        {
            chips.FloorWorldY = -Spec.Mill.StockHeight * WorldScale.MmToM;
            _work.AddChild(chips);
            _mill = new MillCutting(Spec, bridge, _millBody, chips);
            if (HandsOnMill)
            {
                var workholding = new MillWorkholding { Name = "Workholding" };
                AddChild(workholding);
                workholding.Setup(Spec, _millBody, _mill, bridge);
            }
            else
                _mill.MountStock(_voxelStock);
            bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(_mill.OnConfirmedState));
        }
        else
        {
            _work.AddChild(chips);
            var parts = new Node3D { Name = "Parts" };
            _work.AddChild(parts);
            _lathe = new LatheCutting(Spec, bridge, _latheBody, _workpiece, chips, parts);
            if (Spec.ManualSetup)
            {
                var workholding = new LatheWorkholding { Name = "Workholding" };
                AddChild(workholding);
                workholding.Setup(Spec, _latheBody, _lathe, _workpiece, bridge);
            }
            bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(_lathe.OnConfirmedState));
        }
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
        {
            _fingerprintTimer += delta;
            if (_fingerprintTimer < 0.25)
                return;
            _fingerprintTimer = 0.0;
            if (Spec?.Fingerprint() != _fingerprint)
                Rebuild();
            return;
        }

        _mill?.Process(delta);
        _lathe?.Process(delta);

        if (_mill != null && OS.HasEnvironment("CNC_DEBUG_PRINT"))
        {
            _debugTimer += delta;
            if (_debugTimer >= 0.5)
            {
                _debugTimer = 0.0;
                var s = Rig.Bridge.State;
                GD.Print($"[{Spec.MachineName}] connected={Rig.Bridge.IsBridgeConnected} " +
                         $"pos=({s.Position.X:F3},{s.Position.Y:F3},{s.Position.Z:F3},{s.Position.A:F2},{s.Position.B:F2},{s.Position.C:F2}) " +
                         $"tool=T{s.ToolNumber} removed={_mill.RemovedMm3:F0}mm3 seq={s.LastSeq}");
            }
        }
    }

    private double _debugTimer;

    public void StopBackingProcess()
    {
        if (Rig != null)
            MachineProcessController.StopBackingProcess(Rig.Profile);
    }
}
