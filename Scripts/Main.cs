using Godot;
using CncTrainer.Bridge;
using CncTrainer.Material;
using CncTrainer.Chips;
using CncTrainer.Scheduling;
using CncTrainer.Panel;

namespace CncTrainer;

/// <summary>Moves a cube to the bridge's reported tool position, sweeps the
/// workpiece material model between confirmed samples (Phase 2), and shows
/// basic connection/debug info. See PLAN.md Phase 0/1/2.</summary>
public partial class Main : Node3D
{
    [Export] public MaterialKind StockMaterial = MaterialKind.Steel;

    private readonly System.Collections.Generic.List<MachineRig> _rigs = new();
    private BridgeClient _bridge; // the lathe's rig -- Main.cs's cutting logic is still lathe-specific (see PLAN.md)
    private Label _statusLabel;
    private WorkpieceVisual _workpiece;
    private MachineBody _machineBody;
    private ChipSpawner _chips;
    private Node3D _partsContainer;
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
        _bridge = GetNode<BridgeClient>("BridgeClient"); // plain child node now, not a /root/ autoload (see BridgeClient's own doc comment)
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _workpiece = GetNode<WorkpieceVisual>("Workpiece");
        _machineBody = GetNode<MachineBody>("MachineBody");
        _chips = GetNode<ChipSpawner>("ChipSpawner");
        _partsContainer = GetNode<Node3D>("Parts");

        _material = _workpiece.Profile; // built in WorkpieceVisual's own _Ready(), which runs before this (children before parent)

        var lathe = new MachineRig(MachineProfile.Lathe, _bridge);
        _rigs.Add(lathe);

        // Godot owns this machine's backing LinuxCNC/bridge process now --
        // no more manually starting it in its own terminal window before
        // launching the game (see MachineProcessController's own doc
        // comment). Fire-and-forget: BridgeClient already tolerates "not
        // connected yet" and reconnects the moment packets start.
        MachineProcessController.StartBackingProcess(lathe.Profile);

        // Inject the rig into every consumer that used to look the bridge up
        // itself via the fixed "/root/BridgeClient" autoload path -- the
        // actual fix that makes a second live machine (Phase B) possible.
        GetNode<OperatorPanel>("UI/OperatorPanel").Configure(lathe);
        GetNode<ConsoleStand>("ConsoleStand").Configure(lathe);
        GetNode<DebugOverlay>("UI/DebugOverlay").Configure(lathe, this);

        _bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(OnConnectionChanged));
        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));

        if (OS.HasEnvironment("CNC_BENCHMARK"))
            _benchmark = new BenchmarkRunner(this, _material, _workpiece, _chips, _scheduler);
    }

    private BenchmarkRunner _benchmark;

    /// <summary>Main.cs's cutting logic is still lathe-specific (see PLAN.md's
    /// Phase A scope note) -- projects the bridge's axis-generic Position/
    /// VisualPosition down to the (radius, Z) pair LatheRadiusProfile.Sweep
    /// and the world-position mapping actually need.</summary>
    private static Vector2 LatheXZ(AxisVector v) => new(v.Get(AxisId.X), v.Get(AxisId.Z));

    private void OnConfirmedStateUpdated()
    {
        var s = _bridge.State;
        var pos = LatheXZ(s.Position); // CONFIRMED only -- material removal must never use predicted data

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

                if (result.SeveredIndex is int severedIndex)
                    ExtractFinishedPart(severedIndex);
            }
        }
        _lastConfirmedPos = pos;
    }

    /// <summary>A parting cut just severed the stock at `severedIndex` --
    /// build the detached piece (the finished part) into a real, weighed,
    /// collidable RigidBody3D the player can pick up (PlayerController's
    /// "pickupable" group), and remove that range from the still-attached
    /// stock's own profile so WorkpieceVisual stops drawing it there.</summary>
    private void ExtractFinishedPart(int severedIndex)
    {
        var removedRadiiMm = _material.Sever(severedIndex);
        _workpiece.MarkDirty(severedIndex, _material.BucketCount - 1);

        float startZmm = _material.ZAt(severedIndex + 1);
        var built = WorkpiecePartBuilder.Build(removedRadiiMm, _material.StepMm, startZmm, MaterialPreset.Get(StockMaterial).DensityKgPerM3);
        if (built is not WorkpiecePartBuilder.BuiltPart part)
            return; // degenerate (parted off right at the tip) -- nothing to spawn

        var body = new RigidBody3D { Mass = Mathf.Max(0.01f, part.MassKg) };
        _partsContainer.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = part.Mesh, MaterialOverride = _workpiece.StockVisualMaterial });
        body.AddChild(new CollisionShape3D { Shape = part.Collision });
        // Workpiece/Main share an identity transform at the world origin, so
        // the centroid's machine-mm Z converts straight into world space.
        body.Position = new Vector3(part.CentroidZmm * WorldScale.MmToM, 0f, 0f);
        body.AddToGroup("pickupable");

        if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
            GD.Print($"[Main] extracted finished part: mass={part.MassKg:F3}kg centroidZ={part.CentroidZmm:F1}mm buckets={removedRadiiMm.Length}");
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
        var pos = LatheXZ(_bridge.Prediction.VisualPosition); // X = radius (mm), Y(here) = Z along spindle (mm)
        // World units are meters (WorldScale.MmToM) throughout the scene; the
        // underlying machine data stays in mm (LinuxCNC's own units) all the
        // way up to this boundary.
        var toolWorldPos = new Vector3(pos.Y, pos.X, 0f) * WorldScale.MmToM;
        _machineBody.UpdateCarriage(toolWorldPos.X);
        _machineBody.UpdateTool(toolWorldPos.Y, _bridge.State.ToolNumber, delta);

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
                GD.Print($"[Main] connected={_bridge.IsBridgeConnected} pos=({s.Position.X:F3},{s.Position.Z:F3}) seq={s.LastSeq}");
            }
        }
    }

    public JobScheduler Scheduler => _scheduler;

    private bool _shutdownStarted;

    /// <summary>Fires when the player closes the standalone game window --
    /// the real play flow (launch the .exe, play, close it), unlike the
    /// Godot editor's own Stop button, which can force-kill the process
    /// without delivering this notification at all (a known Godot
    /// limitation, not something fixable here; stated plainly rather than
    /// silently assumed to always work). Stops every rig's backing
    /// process cleanly before actually quitting, so nothing is left
    /// running in WSL for the next session to clean up.</summary>
    public override void _Notification(int what)
    {
        if (what != (int)NotificationWMCloseRequest || _shutdownStarted)
            return;
        _shutdownStarted = true;

        foreach (var rig in _rigs)
            MachineProcessController.StopBackingProcess(rig.Profile);

        GetTree().Quit();
    }

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
            ? $"BRIDGE CONNECTED\nX={s.Position.X:F3} Z={s.Position.Z:F3} mm\n" +
              $"Feed={s.FeedRate:F1} Spindle={s.SpindleRpm:F0} rpm\n" +
              $"Tool T{s.ToolNumber}  Mode={s.TaskMode}  Estop={s.Estop}\n" +
              $"seq={s.LastSeq} age={_bridge.LastPacketAgeSeconds * 1000:F0} ms"
            : "BRIDGE DISCONNECTED\n(waiting for bridge/main.py...)";
    }
}
