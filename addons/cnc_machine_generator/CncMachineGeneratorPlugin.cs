using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Spec;

namespace CncTrainer.Addons;

/// <summary>
/// Editor dock that designs a machine as a MachineSpec (axes, travels,
/// tools, sizes, physical build) starting from a preset, shows it live, and
/// generates its scene in Scenes/Machines plus its LinuxCNC configuration.
/// </summary>
[Tool]
public partial class CncMachineGeneratorPlugin : EditorPlugin
{
    private const string SpecsDir = "res://Machines/Specs";
    private const string ToolMenuName = "CNC Machine Generator";
    private const float MachineSpacing = 2.5f;

    private readonly record struct ExistingMachine(string File, string Name, string Distro, int StatePort, int CommandPort);

    private EditorDock _dock, _toolDock;
    private DescriptionInspectorPlugin _descriptions;
    private OptionButton _presetSelect;
    private readonly List<System.Func<MachineSpec>> _presetFactories = new();
    private EditorInspector _inspector;
    private ProceduralMachine _preview;
    private Camera3D _previewCamera;
    private float _orbitYaw = 0.6f, _orbitPitch = 0.35f, _orbitDistance = 3.5f;
    private Label _problemsLabel, _statusLabel;
    private CheckBox _addToSceneCheck;
    private Button _generateButton;

    private MachineSpec _draft;
    private string _checkedFingerprint;
    private double _checkTimer;
    private List<ExistingMachine> _existing = new();

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
        _dock.AddChild(Scrolled(BuildDock()));
        AddDock(_dock);
        _dock.MakeVisible();
        AddToolMenuItem(ToolMenuName, Callable.From(() => _dock.MakeVisible()));
        RefreshPresets();
        LoadPreset(0);

        _toolDock = new EditorDock { Title = "Tool Designer", LayoutKey = "cnc_tool_designer", DefaultSlot = EditorDock.DockSlot.RightBl };
        _toolDock.AddChild(Scrolled(new ToolDesigner { AddToMachine = AddToolToDraft }));
        AddDock(_toolDock);

        _descriptions = new DescriptionInspectorPlugin();
        AddInspectorPlugin(_descriptions);
    }

    // A dock shares its tab container with the Inspector: an unscrolled tall
    // dock raises the editor's minimum height past the window, and the other
    // docks' scroll areas then run off-screen.
    private static ScrollContainer Scrolled(Control content)
    {
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(content);
        return scroll;
    }

    /// <summary>Puts a designed tool into the machine being designed, as the next tool number.</summary>
    private string AddToolToDraft(ToolSpec tool)
    {
        if (_draft == null)
            return "Machine Generator'da açık makine yok.";
        bool lathe = _draft.Kind == MachineKind.Lathe;
        if (tool.IsLatheTool != lathe && tool.Kind != ToolKind.Drill)
            return lathe ? "Bu bir freze takımı; açık makine torna." : "Bu bir torna kalemi; açık makine freze.";
        tool.Number = _draft.Tools.Where(t => t != null).Select(t => t.Number).DefaultIfEmpty(0).Max() + 1;
        _draft.Tools.Add(tool);
        _inspector.Edit(null);
        _inspector.Edit(_draft);
        _checkedFingerprint = null;
        return $"{_draft.MachineName} makinesine T{tool.Number} olarak eklendi.";
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(ToolMenuName);
        RemoveDock(_dock);
        _dock.QueueFree();
        RemoveDock(_toolDock);
        _toolDock.QueueFree();
        RemoveInspectorPlugin(_descriptions);
    }

    private Control BuildDock()
    {
        var root = new VBoxContainer { Name = "Machine Generator" };
        root.AddThemeConstantOverride("separation", 4);

        root.AddChild(new Label { Text = "Start from" });
        _presetSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FitToLongestItem = false,
            TooltipText = "Machine to start from: a built-in preset, or one saved with \"Save as preset\" (Machines/Specs)." };
        _presetSelect.ItemSelected += index => LoadPreset((int)index);
        root.AddChild(_presetSelect);

        var previewContainer = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(0, 220),
            TooltipText = "Drag to orbit, scroll to zoom",
        };
        previewContainer.GuiInput += OnPreviewInput;
        root.AddChild(previewContainer);
        var viewport = new SubViewport { OwnWorld3D = true };
        previewContainer.AddChild(viewport);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f) });
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.17f, 0.18f, 0.2f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.5f,
            },
        });
        _previewCamera = new Camera3D { Fov = 40f };
        viewport.AddChild(_previewCamera);
        _preview = new ProceduralMachine { Name = "Preview" };
        viewport.AddChild(_preview);
        UpdatePreviewCamera();

        _inspector = new EditorInspector
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260),
            TooltipText = "Every setting of the machine; each one's description is shown above it.",
        };
        root.AddChild(_inspector);

        _problemsLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = "Red: must be fixed before the machine can be generated. Yellow: worth a look." };
        root.AddChild(_problemsLabel);

        var buttons = new HBoxContainer();
        var saveSpecButton = new Button
        {
            Text = "Save as preset",
            TooltipText = $"Saves this spec to {SpecsDir} so it shows up in the list above",
        };
        saveSpecButton.Pressed += OnSaveSpecPressed;
        buttons.AddChild(saveSpecButton);
        _generateButton = new Button { Text = "Generate Machine Scene", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Writes Scenes/Machines/<name>.tscn and linuxcnc-config/gen_<name>.ini / .tbl." };
        _generateButton.Pressed += OnGeneratePressed;
        buttons.AddChild(_generateButton);
        root.AddChild(buttons);

        _addToSceneCheck = new CheckBox { Text = "Add to the open scene", ButtonPressed = true,
            TooltipText = "Also puts the new machine into the scene open in the editor, beside the others." };
        root.AddChild(_addToSceneCheck);

        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, TooltipText = "What the last action did." };
        root.AddChild(_statusLabel);
        return root;
    }

    private void OnPreviewInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            _orbitYaw -= motion.Relative.X * 0.01f;
            _orbitPitch = Mathf.Clamp(_orbitPitch + motion.Relative.Y * 0.01f, -0.2f, 1.4f);
        }
        else if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                _orbitDistance = Mathf.Max(0.8f, _orbitDistance * 0.9f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                _orbitDistance = Mathf.Min(12f, _orbitDistance * 1.1f);
            else
                return;
        }
        else
            return;
        UpdatePreviewCamera();
    }

    private void UpdatePreviewCamera()
    {
        var target = new Vector3(0f, (_draft?.WorkHeightMm ?? 1000f) * WorldScale.MmToM, 0f);
        var offset = new Vector3(Mathf.Sin(_orbitYaw) * Mathf.Cos(_orbitPitch), Mathf.Sin(_orbitPitch),
            Mathf.Cos(_orbitYaw) * Mathf.Cos(_orbitPitch)) * _orbitDistance;
        _previewCamera.Position = target + offset;
        _previewCamera.Basis = Basis.LookingAt(-offset, Vector3.Up);
    }

    private void RefreshPresets()
    {
        _presetSelect.Clear();
        _presetFactories.Clear();
        foreach (var (label, create) in MachinePresets.All)
        {
            _presetSelect.AddItem(label);
            _presetFactories.Add(create);
        }
        if (!DirAccess.DirExistsAbsolute(SpecsDir))
            return;
        foreach (string file in DirAccess.GetFilesAt(SpecsDir).Where(f => f.EndsWith(".tres")))
        {
            string path = $"{SpecsDir}/{file}";
            _presetSelect.AddItem($"Saved: {file.GetBaseName()}");
            _presetFactories.Add(() => (MachineSpec)GD.Load<MachineSpec>(path).DuplicateDeep(Resource.DeepDuplicateMode.All));
        }
    }

    private void LoadPreset(int index)
    {
        _draft = _presetFactories[index]();
        ScanExistingMachines();
        int highestPort = _existing.Count > 0 ? _existing.Max(m => Mathf.Max(m.StatePort, m.CommandPort)) : 9199;
        _draft.StatePort = highestPort + 1;
        _draft.CommandPort = highestPort + 2;

        _preview.Spec = _draft;
        _inspector.Edit(_draft);
        _orbitDistance = _draft.Kind == MachineKind.Mill
            ? Mathf.Max(2.5f, _draft.Mill.BaseWidthMm * WorldScale.MmToM * 2.8f)
            : Mathf.Max(2.5f, _draft.Lathe.BedLengthMm * WorldScale.MmToM * 2.2f);
        UpdatePreviewCamera();
        _checkedFingerprint = null;
        _statusLabel.Text = "";
    }

    /// <summary>Name, distro and ports of every machine scene already in
    /// Scenes/Machines, procedural or hand-written.</summary>
    private void ScanExistingMachines()
    {
        _existing = new List<ExistingMachine>();
        foreach (string file in DirAccess.GetFilesAt(MachineSceneWriter.MachinesDir).Where(f => f.EndsWith(".tscn")))
        {
            var root = GD.Load<PackedScene>($"{MachineSceneWriter.MachinesDir}/{file}")?.Instantiate();
            if (root == null)
                continue;
            if (root is ProceduralMachine { Spec: MachineSpec spec })
                _existing.Add(new ExistingMachine(file, spec.MachineName, spec.WslDistro, spec.StatePort, spec.CommandPort));
            else
                _existing.Add(new ExistingMachine(file, root.Get("MachineName").AsString(), root.Get("WslDistro").AsString(),
                    root.Get("StatePort").AsInt32(), root.Get("CommandPort").AsInt32()));
            root.Free();
        }
    }

    public override void _Process(double delta)
    {
        _checkTimer += delta;
        if (_checkTimer < 0.3 || _draft == null)
            return;
        _checkTimer = 0.0;
        string fingerprint = _draft.Fingerprint();
        if (fingerprint == _checkedFingerprint)
            return;
        _checkedFingerprint = fingerprint;

        var problems = Problems();
        var warnings = Warnings();
        _generateButton.Disabled = problems.Count > 0;
        _problemsLabel.Text = string.Join("\n", problems.Select(p => "✖ " + p).Concat(warnings.Select(w => "⚠ " + w)));
        _problemsLabel.Visible = _problemsLabel.Text.Length > 0;
        _problemsLabel.Modulate = problems.Count > 0 ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.8f, 0.35f);
    }

    private List<string> Problems()
    {
        var problems = _draft.Validate();
        if (ResourceLoader.Exists(MachineSceneWriter.ScenePath(_draft)))
            problems.Add($"{MachineSceneWriter.ScenePath(_draft)} already exists -- pick a different name.");
        foreach (var other in _existing)
            if (other.StatePort == _draft.StatePort || other.StatePort == _draft.CommandPort ||
                other.CommandPort == _draft.StatePort || other.CommandPort == _draft.CommandPort)
                problems.Add($"Port already used by {other.File} ({other.StatePort}/{other.CommandPort}).");
        return problems;
    }

    private List<string> Warnings()
    {
        var warnings = new List<string>();
        string program = "res://linuxcnc-config/" + _draft.DemoProgram;
        if (_draft.DemoProgram.Length > 0 && !FileAccess.FileExists(program))
            warnings.Add($"Demo program linuxcnc-config/{_draft.DemoProgram} does not exist.");
        else if (_draft.DemoProgram.Length > 0)
        {
            string code = System.Text.RegularExpressions.Regex.Replace(FileAccess.GetFileAsString(program).ToUpperInvariant(), @"\([^)]*\)|;.*", "");
            var axes = _draft.Axes.Where(a => a != null).Select(a => a.Letter.ToString()[0]).ToHashSet();
            var missing = "XYZABC".Where(l => !axes.Contains(l) && System.Text.RegularExpressions.Regex.IsMatch(code, $@"(?<![A-Z]){l}\s*[-+.\d#\[]")).ToArray();
            if (missing.Length > 0)
                warnings.Add($"Demo program uses {string.Join(", ", missing)}, which this machine does not have.");
            float topRpm = System.Text.RegularExpressions.Regex.Matches(code, @"(?<![A-Z])S\s*(\d+\.?\d*)")
                .Select(m => float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0f).Max();
            if (topRpm > _draft.EffectiveMaxRpm)
                warnings.Add($"Demo program asks for S{topRpm:0}; the spindle stops at {_draft.EffectiveMaxRpm:0} rpm.");
        }
        return warnings;
    }

    private void OnSaveSpecPressed()
    {
        DirAccess.MakeDirRecursiveAbsolute(SpecsDir);
        string path = $"{SpecsDir}/{_draft.MachineName}.tres";
        var result = ResourceSaver.Save(_draft.DuplicateDeep(Resource.DeepDuplicateMode.All), path);
        _statusLabel.Text = result == Error.Ok ? $"Saved {path}." : $"Failed to save {path} ({result}).";
        if (result != Error.Ok)
            return;
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        int selected = _presetSelect.Selected;
        RefreshPresets();
        _presetSelect.Select(selected);
    }

    private void OnGeneratePressed()
    {
        ScanExistingMachines();
        var problems = Problems();
        if (problems.Count > 0)
        {
            _statusLabel.Text = problems[0];
            return;
        }

        string path = MachineSceneWriter.ScenePath(_draft);
        var result = MachineSceneWriter.Save(_draft);
        if (result != Error.Ok)
        {
            _statusLabel.Text = $"Failed to save {path} ({result}).";
            return;
        }

        string status = $"Created {path} and linuxcnc-config/{_draft.ConfigStem}.ini / .tbl.";
        if (_addToSceneCheck.ButtonPressed)
            status += AddToOpenScene(path, _draft.MachineName);
        _statusLabel.Text = status;

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        ScanExistingMachines();
        _checkedFingerprint = null;
    }

    private static string AddToOpenScene(string scenePath, string machineName)
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not Node3D sceneRoot)
            return "\nNot added to a scene: no 3D scene is open.";
        var node = GD.Load<PackedScene>(scenePath).Instantiate<Node3D>();

        var placed = sceneRoot.GetChildren().OfType<Node3D>()
            .Where(n => n.SceneFilePath.StartsWith(MachineSceneWriter.MachinesDir)).ToArray();
        node.Name = machineName;
        // A procedural machine's origin is on the floor.
        node.Position = new Vector3(placed.Length > 0 ? placed.Max(n => n.Position.X) + MachineSpacing : 0f, WorldScale.FloorY, 0f);
        sceneRoot.AddChild(node);
        node.Owner = sceneRoot;
        EditorInterface.Singleton.MarkSceneAsUnsaved();
        return $"\nAdded '{machineName}' to {sceneRoot.SceneFilePath.GetFile()} (save the scene to keep it).";
    }
}
