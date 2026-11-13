using Godot;
using CncTrainer.Bridge;
using CncTrainer.Material;
using CncTrainer.Chips;

namespace CncTrainer;

/// <summary>Moves a cube to the bridge's reported tool position, sweeps the
/// workpiece material model between confirmed samples (Phase 2), and shows
/// basic connection/debug info. See PLAN.md Phase 0/1/2.</summary>
public partial class Main : Node3D
{
    // Demo stock: 50mm diameter (25mm radius) bar, matches linuxcnc-config's
    // demo_facing_turning.ngc comment ("Stock: 50mm diameter round bar").
    [Export] public float StockRadiusMm = 25.0f;
    [Export] public float StockZMin = -85.0f;
    [Export] public float StockZMax = 5.0f;
    [Export] public MaterialKind StockMaterial = MaterialKind.Steel;

    private BridgeClient _bridge;
    private MeshInstance3D _toolCube;
    private Label _statusLabel;
    private WorkpieceVisual _workpiece;
    private ChipSpawner _chips;
    private LatheRadiusProfile _material;
    private Vector2? _lastConfirmedPos;
    private ChipFormation? _activeChipFormation;
    private double _timeSinceLastCut = 999.0;

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("/root/BridgeClient");
        _toolCube = GetNode<MeshInstance3D>("ToolCube");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _workpiece = GetNode<WorkpieceVisual>("Workpiece");
        _chips = GetNode<ChipSpawner>("ChipSpawner");

        _material = new LatheRadiusProfile(StockRadiusMm, StockZMin, StockZMax);
        _workpiece.Initialize(_material);

        _bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(OnConnectionChanged));
        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));

        if (OS.HasEnvironment("CNC_COMMAND_SELFTEST"))
            _selfTestActive = true;
    }

    // TEMPORARY, to verify the Godot->bridge COMMAND path actually works end to
    // end (every prior phase only verified the bridge->Godot STATE path; the
    // panel's buttons call these exact same BridgeClient methods, so this is a
    // direct test of "do the buttons work" without needing simulated clicks).
    // Remove once the operator panel has been manually clicked through once.
    private bool _selfTestActive;
    private double _selfTestTimer;
    private int _selfTestStep;

    private void RunCommandSelfTest(double delta)
    {
        _selfTestTimer += delta;
        var s = _bridge.State;
        switch (_selfTestStep)
        {
            case 0:
                if (_bridge.IsBridgeConnected)
                {
                    GD.Print("[SelfTest] connected. Sending MachineOn...");
                    _bridge.ResetEstop();
                    _bridge.MachineOn();
                    _selfTestStep = 1;
                    _selfTestTimer = 0;
                }
                break;
            case 1:
                if (_selfTestTimer > 1.0)
                {
                    GD.Print($"[SelfTest] machine_on={s.MachineOn} estop={s.Estop}. Sending HomeAxis(all)...");
                    _bridge.HomeAxis(0xFF);
                    _selfTestStep = 2;
                    _selfTestTimer = 0;
                }
                break;
            case 2:
                if (s.HomedX && s.HomedZ)
                {
                    GD.Print("[SelfTest] homed=true. Sending MDI 'G21 G8 G90' then 'G0 X17.25 Z-23.5'...");
                    _bridge.SendMdi("G21 G8 G90");
                    _bridge.SendMdi("G0 X17.25 Z-23.5");
                    _selfTestStep = 3;
                    _selfTestTimer = 0;
                }
                else if (_selfTestTimer > 25.0)
                {
                    GD.Print("[SelfTest] FAIL: homing did not complete in 25s");
                    _selfTestStep = 99;
                }
                break;
            case 3:
                if (_selfTestTimer > 3.0)
                {
                    bool ok = Mathf.Abs(s.Position.X - 17.25f) < 0.01f && Mathf.Abs(s.Position.Y - (-23.5f)) < 0.01f;
                    GD.Print(ok
                        ? $"[SelfTest] PASS: position is exactly ({s.Position.X:F3},{s.Position.Y:F3}) as commanded via MDI through the bridge."
                        : $"[SelfTest] FAIL: expected (17.250,-23.500), got ({s.Position.X:F3},{s.Position.Y:F3})");
                    _selfTestStep = 99;
                }
                break;
        }
    }

    private void OnConfirmedStateUpdated()
    {
        var s = _bridge.State;
        var pos = s.Position; // CONFIRMED only -- material removal must never use predicted data

        if (_lastConfirmedPos is Vector2 prev)
        {
            // Deliberately not gated on s.InCycle: a real lathe cuts whatever
            // the tool touches regardless of mode (jogging into the spinning
            // stock removes material too -- that's a real mistake an operator
            // can make, and part of the point of a training sim).
            float feedPerRev = s.SpindleRpm > 1.0 ? (float)(s.FeedRate / s.SpindleRpm) : 0f;
            var result = _material.Sweep(prev, pos, s.ToolNoseRadius, feedPerRev, (float)s.SpindleRpm);
            if (result.MaterialRemoved)
            {
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);

                // Surface speed at the current radius -- LinuxCNC gives us no
                // chip data at all (brief section 5's note), so this and
                // feedPerRev are the only two cutting parameters derived from
                // what the bridge actually reports (position, spindle rpm, feed).
                float surfaceSpeedMmPerSec = 2f * Mathf.Pi * Mathf.Abs(pos.X) * (float)s.SpindleRpm / 60f;
                _activeChipFormation = ChipClassifier.Classify(
                    MaterialPreset.Get(StockMaterial), result.DepthOfCutMm, feedPerRev, surfaceSpeedMmPerSec);
                _timeSinceLastCut = 0.0;
            }
        }
        _lastConfirmedPos = pos;
    }

    private void OnConnectionChanged(bool connected)
    {
        UpdateStatusLabel(connected);
    }

    private double _debugPrintTimer;

    public override void _Process(double delta)
    {
        // Cosmetic only: the tool's drawn position uses the PREDICTED/smoothed
        // visual position, never the raw confirmed one directly -- this is the
        // layer that's allowed to be "wrong" for a frame or two. Anything that
        // must never roll back (material removal, Phase 2) reads _bridge.State
        // (confirmed-only) instead, never _bridge.Prediction.
        var pos = _bridge.Prediction.VisualPosition; // X = radius (mm), Z = along spindle (mm)
        _toolCube.Position = new Vector3(pos.Y, pos.X, 0f);

        _workpiece.RebuildDirtyChunks();

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2) // tool left the material: stop spawning shortly after
            _activeChipFormation = null;
        var contactPointWorld = new Vector3(pos.Y, pos.X, 0f);
        _chips.Tick(delta, _activeChipFormation, contactPointWorld);

        UpdateStatusLabel(_bridge.IsBridgeConnected);

        if (_selfTestActive)
            RunCommandSelfTest(delta);

        if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
        {
            _debugPrintTimer += delta;
            if (_debugPrintTimer >= 0.5)
            {
                _debugPrintTimer = 0;
                var s = _bridge.State;
                GD.Print($"[Main] connected={_bridge.IsBridgeConnected} pos=({s.Position.X:F3},{s.Position.Y:F3}) seq={s.LastSeq}");
            }
        }
    }

    private void UpdateStatusLabel(bool connected)
    {
        var s = _bridge.State;
        _statusLabel.Text = connected
            ? $"BRIDGE CONNECTED\nX={s.Position.X:F3} Z={s.Position.Y:F3} mm\n" +
              $"Feed={s.FeedRate:F1} Spindle={s.SpindleRpm:F0} rpm\n" +
              $"Tool T{s.ToolNumber}  Mode={s.TaskMode}  Estop={s.Estop}\n" +
              $"seq={s.LastSeq} age={_bridge.LastPacketAgeSeconds * 1000:F0} ms"
            : "BRIDGE DISCONNECTED\n(waiting for bridge/main.py...)";
    }
}
