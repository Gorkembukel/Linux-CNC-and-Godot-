using Godot;
using CncTrainer.Bridge;
using CncTrainer.Panel;

namespace CncTrainer;

/// <summary>The workshop: shared environment (floor/light) plus whichever
/// machines and the player are instanced into it. Machine-specific logic
/// (cutting, chips, part extraction, its own backing process) lives on
/// each machine's own scene/script now (see LatheMachine.cs) -- this only
/// wires the workshop-level UI (2D operator panel, debug overlay, status
/// label) to whichever machine it's pointed at, and fans shutdown out to
/// every machine when the player closes the game.</summary>
public partial class Main : Node3D
{
    private LatheMachine _lathe;
    private Label _statusLabel;

    public override void _Ready()
    {
        _lathe = GetNode<LatheMachine>("Lathe");
        _statusLabel = GetNode<Label>("UI/StatusLabel");

        // Inject the rig into every consumer that used to look the bridge up
        // itself via the fixed "/root/BridgeClient" autoload path -- the
        // actual fix that makes a second live machine (Phase B) possible.
        GetNode<OperatorPanel>("UI/OperatorPanel").Configure(_lathe.Rig);
        GetNode<DebugOverlay>("UI/DebugOverlay").Configure(_lathe.Rig, _lathe.Scheduler);

        _lathe.Rig.Bridge.Connect(BridgeClient.SignalName.ConnectionChanged, Callable.From<bool>(UpdateStatusLabel));
    }

    public override void _Process(double delta)
    {
        UpdateStatusLabel(_lathe.Rig.Bridge.IsBridgeConnected);
    }

    private bool _shutdownStarted;

    /// <summary>Fires when the player closes the standalone game window --
    /// the real play flow (launch the .exe, play, close it), unlike the
    /// Godot editor's own Stop button, which can force-kill the process
    /// without delivering this notification at all (a known Godot
    /// limitation, not something fixable here; stated plainly rather than
    /// silently assumed to always work). Stops every machine's backing
    /// process cleanly before actually quitting, so nothing is left
    /// running in WSL for the next session to clean up.</summary>
    public override void _Notification(int what)
    {
        if (what != (int)NotificationWMCloseRequest || _shutdownStarted)
            return;
        _shutdownStarted = true;

        foreach (var child in GetChildren())
            if (child is IMachine machine)
                machine.StopBackingProcess();

        GetTree().Quit();
    }

    private void UpdateStatusLabel(bool connected)
    {
        var s = _lathe.Rig.Bridge.State;
        _statusLabel.Text = connected
            ? $"BRIDGE CONNECTED\nX={s.Position.X:F3} Z={s.Position.Z:F3} mm\n" +
              $"Feed={s.FeedRate:F1} Spindle={s.SpindleRpm:F0} rpm\n" +
              $"Tool T{s.ToolNumber}  Mode={s.TaskMode}  Estop={s.Estop}\n" +
              $"seq={s.LastSeq} age={_lathe.Rig.Bridge.LastPacketAgeSeconds * 1000:F0} ms"
            : "BRIDGE DISCONNECTED\n(waiting for bridge/main.py...)";
    }
}
