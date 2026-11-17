using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Material;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Addons;

/// <summary>
/// Editor dock that designs a tool: start from a catalogue tool, a saved
/// one, a blank custom lathe insert or a mill form cutter; edit its numbers
/// in the inspector or draw its shape on the 2D canvas; see it in 3D; save
/// it to Machines/Tools, or add it to the machine being designed in the
/// Machine Generator.
/// </summary>
[Tool]
public partial class ToolDesigner : VBoxContainer
{
    public const string ToolsDir = "res://Machines/Tools";

    /// <summary>Set by the plugin: adds a copy of the tool to the machine draft; returns what happened.</summary>
    public System.Func<ToolSpec, string> AddToMachine;

    private readonly List<System.Func<ToolSpec>> _starts = new();
    private OptionButton _startSelect;
    private EditorInspector _inspector;
    private ProfileCanvas _canvas;
    private Node3D _previewRoot;
    private Camera3D _camera;
    private Label _problem, _status;
    private LineEdit _name;
    private ToolSpec _tool;
    private string _shown;
    private double _timer;
    private float _yaw = 0.7f, _pitch = 0.4f, _distance = 0.25f;

    public ToolDesigner()
    {
        Name = "Tool Designer";
        AddThemeConstantOverride("separation", 4);

        AddChild(new Label { Text = "Başlangıç" });
        _startSelect = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false };
        _startSelect.ItemSelected += index => Load((int)index);
        AddChild(_startSelect);

        _canvas = new ProfileCanvas { CustomMinimumSize = new Vector2(0, 230), ClipContents = true, TooltipText = "Tekerlek: yakınlaş  Orta tuş: kaydır  Özel şekilde: tıkla ekle / sürükle / sağ tık sil" };
        _canvas.Edited = () => _shown = null;
        AddChild(_canvas);

        var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(0, 170), TooltipText = "Sürükle: döndür  Tekerlek: yakınlaş" };
        container.GuiInput += OnPreviewInput;
        AddChild(container);
        var viewport = new SubViewport { OwnWorld3D = true };
        container.AddChild(viewport);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45f, 35f, 0f) });
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
        _camera = new Camera3D { Fov = 35f, Near = 0.002f };
        viewport.AddChild(_camera);
        _previewRoot = new Node3D();
        viewport.AddChild(_previewRoot);

        _inspector = new EditorInspector { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 240) };
        AddChild(_inspector);

        _problem = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.45f, 0.4f) };
        AddChild(_problem);

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Ad" });
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "dosya adı" };
        row.AddChild(_name);
        AddChild(row);

        var buttons = new HBoxContainer();
        var save = new Button { Text = "Kaydet", TooltipText = $"{ToolsDir}/<ad>.tres olarak kaydeder" };
        save.Pressed += Save;
        buttons.AddChild(save);
        var add = new Button { Text = "Makine taslağına ekle", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Machine Generator'da açık makineye sıradaki takım numarasıyla ekler" };
        add.Pressed += () => _status.Text = _tool == null ? "" : AddToMachine?.Invoke((ToolSpec)_tool.DuplicateDeep(Resource.DeepDuplicateMode.All)) ?? "Machine Generator açık değil.";
        buttons.AddChild(add);
        AddChild(buttons);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);

        RefreshStarts();
        Load(0);
    }

    private void RefreshStarts()
    {
        _startSelect.Clear();
        _starts.Clear();
        void Add(string label, System.Func<ToolSpec> create)
        {
            _startSelect.AddItem(label);
            _starts.Add(create);
        }
        Add("Yeni özel torna kalemi (çizerek)", () =>
        {
            var tool = new ToolSpec(1, ToolKind.Turning, "Custom form tool", 0f, 0.2f, color: new Color(0.85f, 0.68f, 0.25f));
            tool.Geometry = new ToolGeometry
            {
                Insert = InsertShape.Custom, NoseRadiusMm = 0.2f,
                OutlineMm = new[] { new Vector2(0f, 0f), new Vector2(2f, -3f), new Vector2(10f, -3f), new Vector2(10f, 8f), new Vector2(3f, 8f) },
            };
            return tool;
        });
        Add("Yeni freze form kesici (çizerek)", () =>
        {
            var tool = new ToolSpec(1, ToolKind.EndMill, "Custom form cutter", 12f, lengthMm: 40f) { Flutes = 4 };
            tool.Geometry = new ToolGeometry { MillProfileMm = new[] { Vector2.Zero, new Vector2(3f, 0f), new Vector2(6f, 3f), new Vector2(6f, 12f), new Vector2(4f, 40f) } };
            return tool;
        });
        foreach (var (label, create) in ToolLibrary.Lathe)
            Add("Torna: " + label, () => create(1));
        foreach (var (label, create) in ToolLibrary.Mill)
            Add("Freze: " + label, () =>
            {
                var tool = create(1);
                tool.Geometry = new ToolGeometry();
                return tool;
            });
        if (DirAccess.DirExistsAbsolute(ToolsDir))
            foreach (string file in DirAccess.GetFilesAt(ToolsDir).Where(f => f.EndsWith(".tres")))
            {
                string path = $"{ToolsDir}/{file}";
                Add("Kayıtlı: " + file.GetBaseName(), () => (ToolSpec)GD.Load<ToolSpec>(path).DuplicateDeep(Resource.DeepDuplicateMode.All));
            }
    }

    private void Load(int index)
    {
        _tool = _starts[index]();
        _canvas.Tool = _tool;
        _inspector.Edit(_tool);
        _name.Text = (_tool.Description ?? "tool").ToLowerInvariant().Replace(' ', '_').Replace(',', '_').Replace('°', 'd').Replace('/', '_');
        _shown = null;
        _status.Text = "";
    }

    private void OnPreviewInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            _yaw -= motion.Relative.X * 0.01f;
            _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.01f, -1.4f, 1.4f);
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp })
            _distance = Mathf.Max(0.03f, _distance * 0.9f);
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown })
            _distance = Mathf.Min(2f, _distance * 1.1f);
        else
            return;
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        var target = _tool != null && !_tool.IsLatheTool ? new Vector3(0f, 0.03f, 0f) : new Vector3(0.01f, 0.02f, 0f);
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        _camera.Position = target + offset;
        _camera.Basis = Basis.LookingAt(-offset, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _timer += delta;
        if (_timer < 0.25 || _tool == null)
            return;
        _timer = 0.0;
        string fingerprint = MachineSpec.FingerprintOf(_tool);
        if (fingerprint == _shown)
            return;
        _shown = fingerprint;

        _problem.Text = ProfileCanvas.Problem(_tool) ?? "";
        foreach (var child in _previewRoot.GetChildren())
            child.QueueFree();
        if (_problem.Text.Length == 0)
            _previewRoot.AddChild(_tool.IsLatheTool ? ToolMeshBuilder.LatheTool(_tool, 0.06f) : ToolMeshBuilder.MillTool(_tool));
        PlaceCamera();
        _canvas.QueueRedraw();
    }

    private void Save()
    {
        if (_tool == null || ProfileCanvas.Problem(_tool) is string problem)
        {
            _status.Text = "Kaydedilmedi: " + (ProfileCanvas.Problem(_tool) ?? "takım yok");
            return;
        }
        string name = _name.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_.-]+$"))
        {
            _status.Text = "Ad yalnızca harf, rakam, _ . - içerebilir.";
            return;
        }
        DirAccess.MakeDirRecursiveAbsolute(ToolsDir);
        string path = $"{ToolsDir}/{name}.tres";
        var result = ResourceSaver.Save(_tool.DuplicateDeep(Resource.DeepDuplicateMode.All), path);
        _status.Text = result == Error.Ok ? $"Kaydedildi: {path}" : $"Kaydedilemedi ({result}).";
        if (result != Error.Ok)
            return;
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        int selected = _startSelect.Selected;
        RefreshStarts();
        _startSelect.Select(selected);
    }
}
