using Godot;

namespace CncTrainer.Addons;

/// <summary>
/// Simple editor plugin: generates a new machine scene file from an
/// existing one used as a template (default: Scenes/Machines/Lathe.tscn),
/// with the new scene's own [Export] parameters (MachineName/WslDistro/
/// StatePort/CommandPort/RateHz -- see LatheMachine.cs) filled in from this
/// dock instead of hand-authoring a .tscn + wiring it up every time a new
/// machine is added (see PLAN.md). Uses Node.Set(name, value) rather than
/// casting to a specific machine script type, so it works for any future
/// machine whose root script exposes the same five fields, not just the
/// lathe's.
/// </summary>
[Tool]
public partial class CncMachineGeneratorPlugin : EditorPlugin
{
    private EditorDock _dock;
    private LineEdit _templateInput;
    private LineEdit _nameInput;
    private LineEdit _distroInput;
    private SpinBox _statePortInput;
    private SpinBox _commandPortInput;
    private SpinBox _rateHzInput;
    private Label _statusLabel;

    public override void _EnterTree()
    {
        _dock = new EditorDock { Title = "Machine Generator", DefaultSlot = EditorDock.DockSlot.RightUl };
        _dock.AddChild(BuildDock());
        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
    }

    private Control BuildDock()
    {
        var root = new VBoxContainer { Name = "Machine Generator" };
        root.AddThemeConstantOverride("separation", 4);

        root.AddChild(new Label { Text = "CNC Machine Generator", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        root.AddChild(new Label { Text = "Template scene" });
        _templateInput = new LineEdit { Text = "res://Scenes/Machines/Lathe.tscn" };
        root.AddChild(_templateInput);

        root.AddChild(new Label { Text = "Machine name" });
        _nameInput = new LineEdit { PlaceholderText = "e.g. Mill" };
        root.AddChild(_nameInput);

        root.AddChild(new Label { Text = "WSL distro" });
        _distroInput = new LineEdit { Text = "Ubuntu" };
        root.AddChild(_distroInput);

        root.AddChild(new Label { Text = "State port" });
        _statePortInput = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = 9210 };
        root.AddChild(_statePortInput);

        root.AddChild(new Label { Text = "Command port" });
        _commandPortInput = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = 9211 };
        root.AddChild(_commandPortInput);

        root.AddChild(new Label { Text = "Rate (Hz)" });
        _rateHzInput = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 100 };
        root.AddChild(_rateHzInput);

        var generateButton = new Button { Text = "Generate Machine Scene" };
        generateButton.Pressed += OnGeneratePressed;
        root.AddChild(generateButton);

        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_statusLabel);

        return root;
    }

    private void OnGeneratePressed()
    {
        string machineName = _nameInput.Text.Trim();
        if (machineName.Length == 0)
        {
            _statusLabel.Text = "Enter a machine name first.";
            return;
        }

        string templatePath = _templateInput.Text.Trim();
        string savePath = $"res://Scenes/Machines/{machineName}.tscn";

        if (ResourceLoader.Exists(savePath))
        {
            _statusLabel.Text = $"{savePath} already exists -- pick a different name.";
            return;
        }

        var template = GD.Load<PackedScene>(templatePath);
        if (template == null)
        {
            _statusLabel.Text = $"Couldn't load template scene at {templatePath}.";
            return;
        }

        var instance = template.Instantiate();
        instance.Name = machineName;
        instance.Set("MachineName", machineName);
        instance.Set("WslDistro", _distroInput.Text.Trim());
        instance.Set("StatePort", (int)_statePortInput.Value);
        instance.Set("CommandPort", (int)_commandPortInput.Value);
        instance.Set("RateHz", _rateHzInput.Value);

        // BridgeClient binds its sockets in its own _Ready(), before the
        // machine root's, so it can't pick the ports up from the root at
        // runtime -- they have to be written into the scene here too.
        var bridgeClient = instance.GetNodeOrNull("BridgeClient");
        bridgeClient?.Set("StatePort", (int)_statePortInput.Value);
        bridgeClient?.Set("CommandPort", (int)_commandPortInput.Value);

        var packed = new PackedScene();
        var packResult = packed.Pack(instance);
        instance.QueueFree();

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

        _statusLabel.Text = $"Created {savePath}.";
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }
}
