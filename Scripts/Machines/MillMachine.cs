using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using CncTrainer.Material;

namespace CncTrainer;

/// <summary>
/// One 3-axis mill as its own scene (`Scenes/Machines/Mill.tscn`). Exposes
/// the same launch [Export]s as LatheMachine, so the machine-scene generator
/// plugin can use this scene as a template too.
/// </summary>
public partial class MillMachine : Node3D, IMachine
{
    [Export] public string MachineName = "Mill";
    [Export] public string WslDistro = "Ubuntu-26.04";
    [Export] public int StatePort = 9202;
    [Export] public int CommandPort = 9203;
    [Export] public double RateHz = 100.0;
    [Export] public string IniFile = "sim_mill.ini";
    [Export] public MaterialKind StockMaterial = MaterialKind.Aluminum;

    public MachineRig Rig { get; private set; }

    private BridgeClient _bridge;
    private MillStockVisual _stock;
    private MillBody _body;
    private ChipSpawner _chips;
    private Vector3? _lastConfirmedPos;
    private ChipFormation? _activeChipFormation;
    private double _timeSinceLastCut = 999.0;

    private static readonly (AxisId Axis, byte Joint)[] MillAxes =
        { (AxisId.X, (byte)0), (AxisId.Y, (byte)1), (AxisId.Z, (byte)2) };

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("BridgeClient");
        _stock = GetNode<MillStockVisual>("Stock");
        _body = GetNode<MillBody>("MillBody");
        _chips = GetNode<ChipSpawner>("ChipSpawner");

        var profile = new MachineProfile(MachineName, MillAxes, WslDistro, StatePort, CommandPort, RateHz, IniFile);
        Rig = new MachineRig(profile, _bridge);
        MachineProcessController.StartBackingProcess(profile);

        GetNode<ConsoleStand>("ConsoleStand").Configure(Rig);
        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));
    }

    private static Vector3 MachineXYZ(AxisVector v) => new(v.X, v.Y, v.Z);

    private void OnConfirmedStateUpdated()
    {
        var s = _bridge.State;
        var pos = MachineXYZ(s.Position);
        // The mill's tool table stores the cutter DIAMETER in the field the
        // wire protocol calls ToolNoseRadius.
        float toolRadiusMm = s.ToolNoseRadius / 2f;

        if (_lastConfirmedPos is Vector3 prev && toolRadiusMm > 0f)
        {
            var result = _stock.Heightmap.Sweep(prev, pos, toolRadiusMm);
            if (result.MaterialRemoved)
            {
                _stock.MarkDirty(result);

                float rpm = (float)s.SpindleRpm;
                float feedPerRev = rpm > 1f ? MachineXYZ(s.Velocity).Length() * 60f / rpm : 0f;
                float surfaceSpeedMmPerSec = 2f * Mathf.Pi * toolRadiusMm * rpm / 60f;
                _activeChipFormation = ChipClassifier.Classify(
                    MaterialPreset.Get(StockMaterial), result.MaxDepthMm, feedPerRev, surfaceSpeedMmPerSec);
                _timeSinceLastCut = 0.0;

                if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
                    _debugRemovedMm3 += result.VolumeMm3;
            }
        }
        _lastConfirmedPos = pos;
    }

    public override void _Process(double delta)
    {
        var p = MachineXYZ(_bridge.Prediction.VisualPosition);
        var tipLocal = new Vector3(p.X, p.Z, -p.Y) * WorldScale.MmToM;
        _body.UpdateTool(tipLocal, _bridge.State.ToolNoseRadius);

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2)
            _activeChipFormation = null;

        _stock.RebuildDirtyChunks();
        _chips.Tick(delta, _activeChipFormation, tipLocal);

        if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
        {
            _debugPrintTimer += delta;
            if (_debugPrintTimer >= 0.5)
            {
                _debugPrintTimer = 0;
                var s = _bridge.State;
                GD.Print($"[{MachineName}] connected={_bridge.IsBridgeConnected} pos=({s.Position.X:F3},{s.Position.Y:F3},{s.Position.Z:F3}) " +
                         $"tool=T{s.ToolNumber} removed={_debugRemovedMm3:F0}mm3 seq={s.LastSeq}");
            }
        }
    }

    private double _debugPrintTimer;
    private double _debugRemovedMm3;

    public void StopBackingProcess() => MachineProcessController.StopBackingProcess(Rig.Profile);
}
