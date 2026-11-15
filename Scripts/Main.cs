using System.Collections.Generic;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Panel;

namespace CncTrainer;

/// <summary>The workshop: shared environment (floor/light) plus whichever
/// machines and the player are instanced into it. Machine-specific logic
/// lives on each machine's own scene/script; this only points the
/// workshop-level UI (2D operator panel, debug overlay, status label) at
/// the machine picked in the operator panel, and fans shutdown out to every
/// machine when the player closes the game.</summary>
public partial class Main : Node3D
{
    private readonly List<IMachine> _machines = new();
    private IMachine _selected;
    private Label _statusLabel;
    private DebugOverlay _debugOverlay;

    public override void _Ready()
    {
        foreach (var child in GetChildren())
            if (child is IMachine { Rig: not null } machine)
                _machines.Add(machine);

        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _debugOverlay = GetNode<DebugOverlay>("UI/DebugOverlay");

        foreach (var machine in _machines)
        {
            string name = machine.Rig.Profile.Name;
            machine.Rig.Bridge.EventReceived += (kind, text) => Player.PlayerController.Notify(this, $"{name}: {text}");
        }

        var panel = GetNode<OperatorPanel>("UI/OperatorPanel");
        panel.MachineSelected += OnMachineSelected;
        panel.SetMachines(_machines);
    }

    private void OnMachineSelected(IMachine machine)
    {
        _selected = machine;
        _debugOverlay.Configure(machine.Rig, machine.Scheduler);
    }

    public override void _Process(double delta)
    {
        var profile = _selected.Rig.Profile;
        var bridge = _selected.Rig.Bridge;
        var s = bridge.State;

        string position = "";
        foreach (var (axis, _) in profile.Axes)
            position += $"{axis}={s.Position.Get(axis):F3} ";

        _statusLabel.Text = bridge.IsBridgeConnected
            ? $"{profile.Name.ToUpperInvariant()} CONNECTED\n{position}mm\n" +
              $"Feed={s.FeedRate:F1} Spindle={s.SpindleRpm:F0} rpm\n" +
              $"Tool T{s.ToolNumber}  Mode={s.TaskMode}  Estop={s.Estop}\n" +
              $"seq={s.LastSeq} age={bridge.LastPacketAgeSeconds * 1000:F0} ms"
            : $"{profile.Name.ToUpperInvariant()} DISCONNECTED\n(waiting for bridge/main.py...)";
    }

    private bool _shutdownStarted;

    /// <summary>Fires when the player closes the standalone game window --
    /// the real play flow, unlike the Godot editor's own Stop button, which
    /// can force-kill the process without delivering this notification at
    /// all (a known Godot limitation). Stops every machine's backing
    /// process before quitting, so nothing is left running in WSL.</summary>
    public override void _Notification(int what)
    {
        if (what != (int)NotificationWMCloseRequest || _shutdownStarted)
            return;
        _shutdownStarted = true;

        foreach (var machine in _machines)
            machine.StopBackingProcess();

        GetTree().Quit();
    }
}
