using Godot;
using CncTrainer.Bridge;
using CncTrainer.Material;
using CncTrainer.Chips;
using CncTrainer.Scheduling;

namespace CncTrainer;

/// <summary>
/// One lathe, as its own self-contained scene (`Scenes/Machines/Lathe.tscn`):
/// owns its hardware children (BridgeClient, WorkpieceVisual, MachineBody,
/// ConsoleStand, ChipSpawner, Parts), its own backing LinuxCNC/bridge
/// process, and the lathe-specific cutting/chip/part-extraction logic that
/// used to live directly on Main.cs before the workshop/machine scene split
/// (see PLAN.md). Main.cs (the workshop) only talks to this through `Rig`
/// (for wiring the 2D operator panel / debug overlay, which stay
/// workshop-level UI) and `StopBackingProcess()` (shutdown fan-out).
///
/// [Exports] (MachineName/WslDistro/StatePort/CommandPort/RateHz) are what
/// today's hardcoded MachineProfile.Lathe used to be -- this is the exact
/// set of parameters the machine-scene-generator editor plugin fills in
/// when it creates a new machine scene from this one as a template.
/// </summary>
public partial class LatheMachine : Node3D, IMachine
{
    [Export] public string MachineName = "Lathe";
    [Export] public string WslDistro = "Ubuntu";
    [Export] public int StatePort = 9200;
    [Export] public int CommandPort = 9201;
    [Export] public double RateHz = 100.0;
    [Export] public string IniFile = "sim_lathe.ini";
    [Export] public MaterialKind StockMaterial = MaterialKind.Steel;

    public MachineRig Rig { get; private set; }
    public JobScheduler Scheduler => _scheduler;

    private BridgeClient _bridge;
    private WorkpieceVisual _workpiece;
    private MachineBody _machineBody;
    private ConsoleStand _consoleStand;
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

    private static readonly (AxisId Axis, byte Joint)[] LatheAxes = { (AxisId.X, (byte)0), (AxisId.Z, (byte)1) };

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("BridgeClient");
        _workpiece = GetNode<WorkpieceVisual>("Workpiece");
        _machineBody = GetNode<MachineBody>("MachineBody");
        _consoleStand = GetNode<ConsoleStand>("ConsoleStand");
        _chips = GetNode<ChipSpawner>("ChipSpawner");
        _partsContainer = GetNode<Node3D>("Parts");

        _material = _workpiece.Profile; // built in WorkpieceVisual's own _Ready(), which runs before this (children before parent)

        var profile = new MachineProfile(MachineName, LatheAxes, WslDistro, StatePort, CommandPort, RateHz, IniFile);
        Rig = new MachineRig(profile, _bridge);

        // Godot owns this machine's backing LinuxCNC/bridge process now --
        // no more manually starting it in its own terminal window before
        // launching the game (see MachineProcessController's own doc
        // comment). Fire-and-forget: BridgeClient already tolerates "not
        // connected yet" and reconnects the moment packets start.
        MachineProcessController.StartBackingProcess(Rig.Profile);

        _consoleStand.Configure(Rig);

        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));

        if (OS.HasEnvironment("CNC_BENCHMARK"))
            _benchmark = new BenchmarkRunner(this, _material, _workpiece, _chips, _scheduler);
    }

    private BenchmarkRunner _benchmark;

    /// <summary>Projects the bridge's axis-generic Position/VisualPosition
    /// down to the (radius, Z) pair LatheRadiusProfile.Sweep and the
    /// world-position mapping actually need -- this machine's cutting
    /// logic is still lathe-specific (see PLAN.md's Phase A scope note),
    /// deliberately not forced through a generic N-axis material model.</summary>
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
        // This scene's root sits at the world origin with an identity
        // transform (see Scenes/Main.tscn), so the centroid's machine-mm Z
        // converts straight into this machine's own local/world space.
        body.Position = new Vector3(part.CentroidZmm * WorldScale.MmToM, 0f, 0f);
        body.AddToGroup("pickupable");

        if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
            GD.Print($"[{MachineName}] extracted finished part: mass={part.MassKg:F3}kg centroidZ={part.CentroidZmm:F1}mm buckets={removedRadiiMm.Length}");
    }

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

        if (OS.HasEnvironment("CNC_DEBUG_PRINT"))
        {
            _debugPrintTimer += delta;
            if (_debugPrintTimer >= 0.5)
            {
                _debugPrintTimer = 0;
                var s = _bridge.State;
                GD.Print($"[{MachineName}] connected={_bridge.IsBridgeConnected} pos=({s.Position.X:F3},{s.Position.Z:F3}) seq={s.LastSeq}");
            }
        }
    }

    private double _debugPrintTimer;

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

    /// <summary>Called by Main (the workshop) on NotificationWMCloseRequest.</summary>
    public void StopBackingProcess() => MachineProcessController.StopBackingProcess(Rig.Profile);
}
