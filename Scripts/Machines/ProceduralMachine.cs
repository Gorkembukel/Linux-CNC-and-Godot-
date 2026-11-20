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
public partial class ProceduralMachine : Node3D, IMachine, Save.ISaveable
{
    /// <summary>Everything that defines this machine; its body, setup and LinuxCNC configuration are built from it.</summary>
    [Export] public MachineSpec Spec { get; set; }

    public MachineRig Rig { get; private set; }
    public MachineSafety Safety { get; private set; }
    public CuttingProcess Cutting { get; private set; }
    /// <summary>What the opened program's path is checked against before it
    /// runs (GRAPH page).</summary>
    public System.Collections.Generic.List<Machining.IPathObstacle> PathObstacles { get; } = new();
    public Scheduling.JobScheduler Scheduler => null;

    private const int EditorPreviewNodes = 150_000;
    private const int RuntimeNodes = 6_000_000;

    private Node3D _work;
    private ProceduralMillBody _millBody;
    private ProceduralLatheBody _latheBody;
    private VoxelStockVisual _voxelStock;
    private WorkpieceVisual _workpiece;
    private string _fingerprint;
    private double _fingerprintTimer;

    /// <summary>Group every running machine joins.</summary>
    public const string Group = "machines";

    public override void _Ready()
    {
        AddToGroup(Group);
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
        Spec.WriteConfigFiles(CncTrainer.Save.SaveGame.TakeControl(Spec.MachineName));

        // BridgeClient binds its sockets in its own _Ready.
        var bridge = new BridgeClient { Name = "BridgeClient", StatePort = Spec.StatePort, CommandPort = Spec.CommandPort };
        AddChild(bridge);

        var axes = AxisDef.InJointOrder(Spec.AxisDefs()).Select((a, joint) => (a.Letter, (byte)joint)).ToArray();
        var profile = new MachineProfile(Spec.MachineName, axes, Spec.WslDistro, Spec.StatePort, Spec.CommandPort,
            Spec.RateHz, Spec.ConfigStem + ".ini", Spec.DemoProgram);
        Rig = new MachineRig(profile, bridge);
        Safety = new MachineSafety(this, bridge);
        AddChild(new Audio.MachineAudio { Name = "MachineAudio", Rig = Rig, Position = new Vector3(0f, 1.2f, 0f) });
        MachineProcessController.StartBackingProcess(profile);
        GetNodeOrNull<ConsoleStand>("ConsoleStand")?.Configure(Rig);

        var chips = new ChipSpawner { Name = "ChipSpawner" };
        chips.SpindleTurning = () => System.Math.Abs(bridge.State.SpindleRpm) > 1.0;
        chips.Safety = Safety;
        _work.AddChild(chips);
        if (Spec.Kind == MachineKind.Mill)
        {
            chips.FloorWorldY = -Spec.Mill.StockHeight * WorldScale.MmToM;
            var mill = new MillCutting(Spec, bridge, _millBody, chips);
            Cutting = mill;
            if (HandsOnMill)
            {
                var setup = new MillWorkholding { Name = "Workholding" };
                AddChild(setup);
                setup.Setup(Spec, _millBody, mill, bridge, Safety);
                PathObstacles.Add(setup);
            }
            else
                mill.MountStock(_voxelStock);
        }
        else
        {
            var parts = new Node3D { Name = "Parts" };
            _work.AddChild(parts);
            var lathe = new LatheCutting(Spec, bridge, _latheBody, _workpiece, chips, parts);
            Cutting = lathe;
            PathObstacles.Add(lathe);
            if (Spec.ManualSetup)
            {
                var setup = new LatheWorkholding { Name = "Workholding" };
                AddChild(setup);
                setup.Setup(Spec, _latheBody, lathe, _workpiece, bridge, Safety);
            }
        }
        Rig.Monitor = new Machining.CutMonitor(Spec, Cutting, Safety);
        Cutting.Collided += reason => Safety.Trip(IncidentKind.Crash, reason);
        Cutting.Surface = () => Rig.Monitor.Surface;
        Cutting.DeflectionMm = () => Rig.Monitor.DeflectionMm;
        Cutting.PartFinished = Rig.Monitor.ResetQuality;
        bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(Cutting.OnConfirmedState));
    }

    /// <summary>The control's memory: offsets LinuxCNC holds and the wear
    /// kept here. Only once the control has reported its tool table.</summary>
    public Godot.Collections.Dictionary Save()
    {
        var data = new Godot.Collections.Dictionary { ["machine"] = Spec?.MachineName ?? "" };
        if (Rig?.Bridge is not { IsBridgeConnected: true, Offsets.Received: true } bridge)
            return data;
        var s = bridge.State;
        data["control"] = CncTrainer.Save.SaveCodec.Control(new ControlMemory(s.G5xIndex,
            new[] { s.G5xOffset.X, s.G5xOffset.Y, s.G5xOffset.Z, s.G5xOffset.A, s.G5xOffset.B, s.G5xOffset.C },
            bridge.Offsets.Entries.ToDictionary(e => e.Number, e => ((float)e.X, (float)e.Z))));
        data["wear"] = new Godot.Collections.Array(bridge.Offsets.Wears.Select(w => (Variant)new Vector3(w.Tool, (int)w.Axis, w.Wear)));
        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (Rig?.Bridge == null || !data.TryGetValue("wear", out var wear))
            return;
        foreach (var w in wear.AsGodotArray().Select(v => v.AsVector3()))
            Rig.Bridge.Offsets.RestoreWear((int)w.X, (AxisId)(int)w.Y, w.Z);
    }

    private double _eyesWarned;

    /// <summary>Chips fly while it cuts: anyone standing at the machine
    /// without safety glasses goes on the record (once a minute).</summary>
    private void CheckEyes(bool cutting, double delta)
    {
        _eyesWarned -= delta;
        if (!cutting || _eyesWarned > 0 || !Workshop.WorkshopSettings.GlassesRule || GetTree().GetFirstNodeInGroup("player") is not Player.PlayerController player)
            return;
        if (!player.WearingGlasses && player.GlobalPosition.DistanceTo(GlobalPosition) < 3.5f)
        {
            Safety.Record(IncidentKind.NoGlasses, "GÖZLÜK YOK: tezgah keserken koruyucu gözlük tak (talaş gözüne gelebilir)");
            _eyesWarned = 60.0;
        }
    }

    private double _indexTimer;

    /// <summary>A Fanuc-style turret indexes on the T word by itself: the
    /// tool change is confirmed without TOOL CHG END.</summary>
    private void IndexTurret(double delta)
    {
        if (!Spec.FanucToolCalls || Rig?.Bridge is not { } bridge || !bridge.State.ToolChangePending)
        {
            _indexTimer = 0.0;
            return;
        }
        _indexTimer += delta;
        if (_indexTimer < TurretIndexSeconds)
            return;
        _indexTimer = 0.0;
        bridge.ConfirmToolChange();
    }

    /// <summary>How long a turret index takes before the control is told
    /// it is done.</summary>
    private const double TurretIndexSeconds = 0.6;

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

        Cutting?.Process(delta);
        IndexTurret(delta);
        if (Rig?.Monitor is { } monitor)
        {
            monitor.WearFactor = Rig.Bridge.State.Coolant ? Machining.CutMonitor.CoolantWearFactor : 1f;
            monitor.WearScale = Workshop.WorkshopSettings.ToolWearScale;
            monitor.Tick(delta);
            CheckEyes(monitor.Load.PowerW > 0f, delta);
        }

        if (Cutting is MillCutting mill && OS.HasEnvironment("CNC_DEBUG_PRINT"))
        {
            _debugTimer += delta;
            if (_debugTimer >= 0.5)
            {
                _debugTimer = 0.0;
                var s = Rig.Bridge.State;
                GD.Print($"[{Spec.MachineName}] connected={Rig.Bridge.IsBridgeConnected} " +
                         $"pos=({s.Position.X:F3},{s.Position.Y:F3},{s.Position.Z:F3},{s.Position.A:F2},{s.Position.B:F2},{s.Position.C:F2}) " +
                         $"tool=T{s.ToolNumber} removed={mill.RemovedMm3:F0}mm3 seq={s.LastSeq}");
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
