using Godot;
using CncTrainer.Bridge;
using CncTrainer.Material;

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

    private BridgeClient _bridge;
    private MeshInstance3D _toolCube;
    private Label _statusLabel;
    private WorkpieceVisual _workpiece;
    private LatheRadiusProfile _material;
    private Vector2? _lastConfirmedPos;

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("/root/BridgeClient");
        _toolCube = GetNode<MeshInstance3D>("ToolCube");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _workpiece = GetNode<WorkpieceVisual>("Workpiece");

        _material = new LatheRadiusProfile(StockRadiusMm, StockZMin, StockZMax);
        _workpiece.Initialize(_material);

        _bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(OnConnectionChanged));
        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnConfirmedStateUpdated));
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
                _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);
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
