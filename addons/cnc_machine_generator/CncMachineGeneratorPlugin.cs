using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CncTrainer.Addons;

/// <summary>
/// Editor plugin: generates a new machine scene from an existing one in
/// Scenes/Machines used as a template, filling in the launch parameters
/// every machine root script exports (MachineName / WslDistro / StatePort /
/// CommandPort / RateHz). Everything else (INI file, demo program, visuals)
/// comes from the template. Uses Node.Get/Set by name rather than a
/// specific script type, so any machine scene works as a template.
/// </summary>
[Tool]
public partial class CncMachineGeneratorPlugin : EditorPlugin
{
    private const string MachinesDir = "res://Scenes/Machines";
    private const string ToolMenuName = "CNC Machine Generator";
    private const float MachineSpacing = 1.6f;

    private readonly record struct MachineInfo(string Path, string Name, string Distro, int StatePort, int CommandPort, double RateHz);

    private EditorDock _dock;
    private OptionButton _templateSelect;
    private LineEdit _nameInput;
    private LineEdit _distroInput;
    private SpinBox _statePortInput;
    private SpinBox _commandPortInput;
    private SpinBox _rateHzInput;
    private CheckBox _addToSceneCheck;
    private Label _statusLabel;
    private List<MachineInfo> _machines = new();

    public override void _EnterTree()
    {
        // Its own, normally empty slot (below the Inspector) rather than a
        // fourth tab beside Inspector/Signals/Groups: there it overflowed
        // the narrow tab bar and was reported as "the dock doesn't show".
        _dock = new EditorDock
        {
            Title = "Machine Generator",
            LayoutKey = "cnc_machine_generator",
            DefaultSlot = EditorDock.DockSlot.RightBl,
        };
        _dock.AddChild(BuildDock());
        AddDock(_dock);
        _dock.MakeVisible();
        AddToolMenuItem(ToolMenuName, Callable.From(() => _dock.MakeVisible()));
        RefreshMachines();
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(ToolMenuName);
        RemoveDock(_dock);
        _dock.QueueFree();
    }

    private Control BuildDock()
    {
        var root = new VBoxContainer { Name = "Machine Generator" };
        root.AddThemeConstantOverride("separation", 4);

        var templateRow = new HBoxContainer();
        _templateSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _templateSelect.ItemSelected += _ => ApplyTemplateDefaults();
        templateRow.AddChild(_templateSelect);
        var refreshButton = new Button { Text = "Refresh", TooltipText = "Re-read the machine scenes in Scenes/Machines" };
        refreshButton.Pressed += RefreshMachines;
        templateRow.AddChild(refreshButton);
        AddField(root, "Template scene", templateRow);

        _nameInput = new LineEdit { PlaceholderText = "e.g. Mill2" };
        AddField(root, "Machine name", _nameInput);

        _distroInput = new LineEdit { TooltipText = "Each running machine needs its own WSL distro: LinuxCNC allows one instance per distro." };
        AddField(root, "WSL distro", _distroInput);

        _statePortInput = new SpinBox { MinValue = 1024, MaxValue = 65535 };
        AddField(root, "State port", _statePortInput);

        _commandPortInput = new SpinBox { MinValue = 1024, MaxValue = 65535 };
        AddField(root, "Command port", _commandPortInput);

        _rateHzInput = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 100 };
        AddField(root, "Rate (Hz)", _rateHzInput);

        _addToSceneCheck = new CheckBox { Text = "Add to the open scene", ButtonPressed = true };
        root.AddChild(_addToSceneCheck);

        var generateButton = new Button { Text = "Generate Machine Scene" };
        generateButton.Pressed += OnGeneratePressed;
        root.AddChild(generateButton);

        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_statusLabel);

        return root;
    }

    private static void AddField(Container parent, string label, Control field)
    {
        parent.AddChild(new Label { Text = label });
        parent.AddChild(field);
    }

    private void RefreshMachines()
    {
        _machines = new List<MachineInfo>();
        foreach (string file in DirAccess.GetFilesAt(MachinesDir))
        {
            if (!file.EndsWith(".tscn"))
                continue;
            string path = $"{MachinesDir}/{file}";
            var root = GD.Load<PackedScene>(path)?.Instantiate();
            if (root == null)
                continue;
            _machines.Add(new MachineInfo(path, root.Get("MachineName").AsString(), root.Get("WslDistro").AsString(),
                root.Get("StatePort").AsInt32(), root.Get("CommandPort").AsInt32(), root.Get("RateHz").AsDouble()));
            root.Free();
        }

        string previous = _templateSelect.ItemCount > 0 ? _templateSelect.GetItemText(_templateSelect.Selected) : "";
        _templateSelect.Clear();
        foreach (var machine in _machines)
            _templateSelect.AddItem(machine.Path.GetFile());
        for (int i = 0; i < _templateSelect.ItemCount; i++)
            if (_templateSelect.GetItemText(i) == previous)
                _templateSelect.Select(i);

        int highestPort = _machines.Count > 0 ? _machines.Max(m => Mathf.Max(m.StatePort, m.CommandPort)) : 9199;
        _statePortInput.Value = highestPort + 1;
        _commandPortInput.Value = highestPort + 2;
        ApplyTemplateDefaults();
    }

    private void ApplyTemplateDefaults()
    {
        if (_machines.Count == 0)
            return;
        var template = _machines[_templateSelect.Selected];
        _distroInput.Text = template.Distro;
        _rateHzInput.Value = template.RateHz;
    }

    private void OnGeneratePressed()
    {
        if (_machines.Count == 0)
        {
            _statusLabel.Text = $"No machine scenes found in {MachinesDir} to use as a template.";
            return;
        }

        string machineName = _nameInput.Text.Trim();
        // The name ends up in a file name and on a shell command line.
        if (!Regex.IsMatch(machineName, "^[A-Za-z][A-Za-z0-9_]*$"))
        {
            _statusLabel.Text = "Machine name must start with a letter and use only letters, digits and _.";
            return;
        }

        string savePath = $"{MachinesDir}/{machineName}.tscn";
        if (ResourceLoader.Exists(savePath))
        {
            _statusLabel.Text = $"{savePath} already exists -- pick a different name.";
            return;
        }

        string distro = _distroInput.Text.Trim();
        int statePort = (int)_statePortInput.Value, commandPort = (int)_commandPortInput.Value;
        if (distro.Length == 0)
        {
            _statusLabel.Text = "Enter the WSL distro this machine's LinuxCNC runs in.";
            return;
        }
        if (statePort == commandPort)
        {
            _statusLabel.Text = "State port and command port must differ.";
            return;
        }
        foreach (var other in _machines)
            if (other.StatePort == statePort || other.StatePort == commandPort ||
                other.CommandPort == statePort || other.CommandPort == commandPort)
            {
                _statusLabel.Text = $"Port already used by {other.Path.GetFile()} ({other.StatePort}/{other.CommandPort}).";
                return;
            }

        var template = _machines[_templateSelect.Selected];
        var instance = GD.Load<PackedScene>(template.Path).Instantiate();
        instance.Name = machineName;
        instance.Set("MachineName", machineName);
        instance.Set("WslDistro", distro);
        instance.Set("StatePort", statePort);
        instance.Set("CommandPort", commandPort);
        instance.Set("RateHz", _rateHzInput.Value);

        // BridgeClient binds its sockets in its own _Ready(), before the
        // machine root's, so it can't pick the ports up from the root at
        // runtime -- they have to be written into the scene here too.
        var bridgeClient = instance.GetNodeOrNull("BridgeClient");
        bridgeClient?.Set("StatePort", statePort);
        bridgeClient?.Set("CommandPort", commandPort);

        var packed = new PackedScene();
        var packResult = packed.Pack(instance);
        instance.Free();
        if (packResult != Error.Ok)
        {
            _statusLabel.Text = $"Failed to pack the new scene ({packResult}).";
            return;
        }

        var saveResult = ResourceSaver.Save(packed, savePath);
        if (saveResult != Error.Ok)
        {
            _statusLabel.Text = $"Failed to save {savePath} ({saveResult}).";
            return;
        }

        string status = $"Created {savePath}.";
        if (_addToSceneCheck.ButtonPressed)
            status += AddToOpenScene(savePath, machineName);

        var sharedDistro = _machines.Where(m => m.Distro == distro).Select(m => m.Path.GetFile()).ToArray();
        if (sharedDistro.Length > 0)
            status += $"\nWarning: {string.Join(", ", sharedDistro)} also runs in '{distro}'. " +
                      "LinuxCNC allows one instance per distro, so they cannot run at the same time.";
        _statusLabel.Text = status;

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        RefreshMachines();
    }

    private static string AddToOpenScene(string scenePath, string machineName)
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not Node3D sceneRoot)
            return "\nNot added to a scene: no 3D scene is open.";
        if (GD.Load<PackedScene>(scenePath).Instantiate() is not Node3D node)
            return "\nNot added to a scene: the machine root is not a Node3D.";

        var placed = sceneRoot.GetChildren().OfType<Node3D>()
            .Where(n => n.SceneFilePath.StartsWith(MachinesDir)).ToArray();
        node.Name = machineName;
        node.Position = new Vector3(placed.Length > 0 ? placed.Max(n => n.Position.X) + MachineSpacing : 0f, 0f, 0f);
        sceneRoot.AddChild(node);
        node.Owner = sceneRoot;
        EditorInterface.Singleton.MarkSceneAsUnsaved();
        return $"\nAdded '{machineName}' to {sceneRoot.SceneFilePath.GetFile()} (save the scene to keep it).";
    }
}
