using System.Collections.Generic;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Inspection;
using CncTrainer.Panel;
using CncTrainer.Save;
using CncTrainer.Settings;
using CncTrainer.UI;
using CncTrainer.Workshop;

namespace CncTrainer;

/// <summary>The workshop: shared environment (floor/light) plus whichever
/// machines and the player are instanced into it. Machine-specific logic
/// lives on each machine's own scene/script; this only points the
/// workshop-level UI (2D operator panel, debug overlay, status label) at
/// the machine picked in the operator panel, starts the session the menu
/// asked for (free, a work order, a save), saves it, and fans shutdown out
/// to every machine when the workshop closes.</summary>
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
        _panel = panel;
        // Debug only: the 3D consoles are the real controls (F2 shows these).
        panel.Visible = false;
        _statusLabel.Visible = false;

        GameSettings.Apply();
        AddChild(new PauseMenu { Name = "PauseMenu", Workshop = this });
        Begin(Session.Take());
    }

    /// <summary>What the job on the board is, for the save list.</summary>
    private string Scenario => GetTree().GetFirstNodeInGroup(JobBoard.Group) is JobBoard { Active: WorkOrder order }
        ? $"{order.OrderNumber} {order.Title}" : "Serbest atölye";

    // Two frames: carts fill themselves deferred, machines set up in their own _Ready.
    private async void Begin(StartRequest request)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (request?.SaveFile != null)
            SaveGame.ApplyPrepared(this);
        else if (request?.Order is WorkOrder order)
        {
            (GetTree().GetFirstNodeInGroup(JobBoard.Group) as JobBoard)?.Take(order);
            (GetTree().GetFirstNodeInGroup("stock_stations") as StockStation)?.Produce(order.Stock);
        }
    }

    public bool Write(string path, string name) => SaveGame.Write(this, path, name, Scenario);

    private double _sinceAutosave;

    private void Autosave()
    {
        _sinceAutosave = 0.0;
        Write(SaveGame.AutoFile, SaveGame.AutoName);
    }

    /// <summary>Leaves the workshop (autosaved) for the main menu, or quits.</summary>
    public void Leave(bool toMenu)
    {
        Autosave();
        ShutDown();
        if (toMenu)
            GetTree().ChangeSceneToFile(Session.MenuScene);
        else
            GetTree().Quit();
    }

    private OperatorPanel _panel;

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 })
            return;
        bool show = !_panel.Visible;
        _panel.Visible = show;
        _statusLabel.Visible = show;
        Input.MouseMode = show ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private void OnMachineSelected(IMachine machine)
    {
        _selected = machine;
        _debugOverlay.Configure(machine.Rig, machine.Scheduler);
    }

    public override void _Process(double delta)
    {
        _sinceAutosave += delta;
        if (GameSettings.AutosaveMinutes > 0f && _sinceAutosave >= GameSettings.AutosaveMinutes * 60.0)
            Autosave();
        if (_selected == null)
            return;
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
        if (what == (int)NotificationWMCloseRequest && !_shutdownStarted)
            Leave(toMenu: false);
    }

    /// <summary>Leaving the scene any way (a save loaded from the menu
    /// too) stops the machines, so a new workshop starts them clean.</summary>
    public override void _ExitTree() => ShutDown();

    private void ShutDown()
    {
        if (_shutdownStarted)
            return;
        _shutdownStarted = true;
        foreach (var machine in _machines)
            machine.StopBackingProcess();
    }
}
