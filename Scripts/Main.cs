using Godot;
using CncTrainer.Bridge;

namespace CncTrainer;

/// <summary>Phase 0 demo: moves a cube to the bridge's reported tool position and
/// shows basic connection/debug info. See PLAN.md Phase 0/1.</summary>
public partial class Main : Node3D
{
    private BridgeClient _bridge;
    private MeshInstance3D _toolCube;
    private Label _statusLabel;

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("/root/BridgeClient");
        _toolCube = GetNode<MeshInstance3D>("ToolCube");
        _statusLabel = GetNode<Label>("UI/StatusLabel");

        _bridge.Connect(BridgeClient.SignalName.StateUpdated, Callable.From(OnStateUpdated));
        _bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(OnConnectionChanged));
    }

    private void OnStateUpdated()
    {
        var pos = _bridge.State.Position; // X = radius (mm), Z = along spindle (mm)
        // World mapping: machine Z -> world X (spindle axis runs left/right),
        // machine X (radius) -> world Y (up), world Z fixed at 0.
        _toolCube.Position = new Vector3(pos.Y, pos.X, 0f);
    }

    private void OnConnectionChanged(bool connected)
    {
        UpdateStatusLabel(connected);
    }

    private double _debugPrintTimer;

    public override void _Process(double delta)
    {
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
