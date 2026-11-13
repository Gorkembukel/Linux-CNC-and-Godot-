using Godot;
using CncTrainer.Bridge;
using CncTrainer.Material;
using CncTrainer.Chips;
using CncTrainer.Scheduling;

namespace CncTrainer;

/// <summary>Moves a cube to the bridge's reported tool position, sweeps the
/// workpiece material model between confirmed samples (Phase 2), and shows
/// basic connection/debug info. See PLAN.md Phase 0/1/2.</summary>
public partial class Main : Node3D
{
    [Export] public MaterialKind StockMaterial = MaterialKind.Steel;

    private BridgeClient _bridge;
    private MeshInstance3D _toolCube;
    private Label _statusLabel;
    private WorkpieceVisual _workpiece;
    private MachineBody _machineBody;
    private ChipSpawner _chips;
    private LatheRadiusProfile _material;
    private Vector2? _lastConfirmedPos;
    private ChipFormation? _activeChipFormation;
    private double _timeSinceLastCut = 999.0;

    private readonly JobScheduler _scheduler = new();
    private readonly System.Collections.Generic.Queue<double> _recentFrameTimesMs = new();
    private const int FrameTimeWindow = 60;
    private const double TargetFrameMs = 1000.0 / 30.0; // brief section 4: 30 FPS floor

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("/root/BridgeClient");
        _toolCube = GetNode<MeshInstance3D>("ToolCube");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _workpiece = GetNode<WorkpieceVisual>("Workpiece");
        _machineBody = GetNode<MachineBody>("MachineBody");
        _chips = GetNode<ChipSpawner>("ChipSpawner");

        _material = _workpiece.Profile; // built in WorkpieceVisual's own _Ready(), which runs before this (children before parent)

        _bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(OnConnectionChanged));
        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));

        if (OS.HasEnvironment("CNC_BENCHMARK"))
            _benchmark = new BenchmarkRunner(this, _material, _workpiece, _chips, _scheduler);
    }

    private BenchmarkRunner _benchmark;

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
        // World units are meters (WorldScale.MmToM) throughout the scene; the
        // underlying machine data stays in mm (LinuxCNC's own units) all the
        // way up to this boundary.
        var toolWorldPos = new Vector3(pos.Y, pos.X, 0f) * WorldScale.MmToM;
        _toolCube.Position = toolWorldPos;
        _machineBody.UpdateCarriage(toolWorldPos.X);

        _timeSinceLastCut += delta;
        if (_timeSinceLastCut > 0.2) // tool left the material: stop spawning shortly after
            _activeChipFormation = null;
        var contactPointWorld = toolWorldPos;

        // Brief section 4's priority classes: mesh regen is "High" (visibly
        // wrong workpiece geometry is jarring), chip simulation is "Normal"
        // (cosmetic, can lag or get capped under load without breaking
        // anything -- see ChipSpawner's own hard cap for the "Low/deferrable"
        // spirit applied to chips specifically).
        _scheduler.Enqueue(JobPriority.High, () => _workpiece.RebuildDirtyChunks());
        _scheduler.Enqueue(JobPriority.Normal, () => _chips.Tick(delta, _activeChipFormation, contactPointWorld));
        _scheduler.RunFrame();

        TrackFrameTimeAndAdaptBudgets(delta);

        _benchmark?.Tick(delta);

        UpdateStatusLabel(_bridge.IsBridgeConnected);

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

    public JobScheduler Scheduler => _scheduler;

    private void TrackFrameTimeAndAdaptBudgets(double delta)
    {
        double frameMs = delta * 1000.0;
        _recentFrameTimesMs.Enqueue(frameMs);
        while (_recentFrameTimesMs.Count > FrameTimeWindow)
            _recentFrameTimesMs.Dequeue();

        double sum = 0;
        foreach (var t in _recentFrameTimesMs) sum += t;
        double avg = sum / _recentFrameTimesMs.Count;
        _scheduler.AdaptBudgets(avg, TargetFrameMs);
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
