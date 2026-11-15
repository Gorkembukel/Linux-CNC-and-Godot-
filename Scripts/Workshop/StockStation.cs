using Godot;
using CncTrainer.Chips;
using CncTrainer.Procedural;
using CncTrainer.Spec;

namespace CncTrainer.Workshop;

/// <summary>
/// Stock dispenser: the player picks shape, size and material on its panel
/// (E while standing at it), sees the result on the preview monitor, and
/// "ÜRET" drops that piece of stock onto the output tray. Nothing is
/// simulated: the stock simply spawns. The node's origin is on the floor.
/// </summary>
[Tool]
public partial class StockStation : Node3D
{
    private static readonly Vector3 TrayCenter = new(0.75f, 0.82f, 0f);

    private StockShape _shape = StockShape.Block;
    private MaterialKind _material = MaterialKind.Aluminum;
    private Vector3 _sizeMm = new(120f, 80f, 30f);

    private SubViewport _preview;
    private Node3D _previewPivot;
    private Camera3D _previewCamera;
    private Label _previewText;
    private CanvasLayer _panelLayer;
    private readonly HSlider[] _sliders = new HSlider[3];
    private readonly SpinBox[] _spins = new SpinBox[3];
    private readonly Label[] _dimLabels = new Label[3];
    private Control _thirdRow;

    public StockBlank Blank => new(_shape, _shape == StockShape.Block ? _sizeMm : new Vector3(_sizeMm.X, _sizeMm.Y, _sizeMm.Y), _material);
    public bool PanelOpen => _panelLayer != null && _panelLayer.Visible;

    public override void _Ready()
    {
        BodyParts.Clear(this);
        var cabinet = BodyParts.Mat(new Color(0.85f, 0.55f, 0.12f));
        var dark = BodyParts.Mat(new Color(0.13f, 0.13f, 0.15f), 0.3f);
        BodyParts.SolidBox(this, "Cabinet", new Vector3(0.9f, 1.0f, 0.6f), new Vector3(0f, 0.5f, 0f), cabinet);
        BodyParts.SolidBox(this, "MonitorPost", new Vector3(0.08f, 0.35f, 0.08f), new Vector3(0f, 1.175f, -0.2f), dark);
        BodyParts.Box(this, "MonitorFrame", new Vector3(0.74f, 0.5f, 0.04f), new Vector3(0f, 1.55f, -0.18f), dark);
        BodyParts.SolidBox(this, "Tray", new Vector3(0.6f, 0.04f, 0.6f), TrayCenter - new Vector3(0f, 0.02f, 0f), BodyParts.Steel);
        BodyParts.SolidBox(this, "TrayLeg", new Vector3(0.5f, 0.78f, 0.5f), new Vector3(TrayCenter.X, 0.39f, 0f), dark);
        AddChild(new Label3D
        {
            Text = "KÜTÜK İSTASYONU",
            PixelSize = 0.002f,
            FontSize = 48,
            Position = new Vector3(0f, 0.8f, 0.301f),
            Modulate = new Color(0.1f, 0.1f, 0.1f),
        });

        if (Engine.IsEditorHint())
            return;
        AddToGroup("stock_stations");
        BuildPreview();
        AddChild(new MeshInstance3D
        {
            Name = "Screen",
            Mesh = new QuadMesh { Size = new Vector2(0.68f, 0.44f) },
            Position = new Vector3(0f, 1.55f, -0.159f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = _preview.GetTexture(),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });
        BuildPanel();
        RefreshPreview();
    }

    private void BuildPreview()
    {
        _preview = new SubViewport { Size = new Vector2I(680, 440), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_preview);
        _preview.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.07f, 0.09f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            },
        });
        _preview.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45f, -30f, 0f) });
        _previewCamera = new Camera3D { Fov = 35f };
        _preview.AddChild(_previewCamera);
        _previewPivot = new Node3D();
        _preview.AddChild(_previewPivot);
        _previewText = new Label { Position = new Vector2(14, 10) };
        _previewText.AddThemeFontSizeOverride("font_size", 22);
        _previewText.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.7f));
        _preview.AddChild(_previewText);
    }

    private void BuildPanel()
    {
        _panelLayer = new CanvasLayer { Visible = false };
        AddChild(_panelLayer);
        var panel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f };
        panel.OffsetLeft = -260; panel.OffsetRight = 260; panel.OffsetTop = -300; panel.OffsetBottom = 300;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.11f, 0.12f, 0.14f, 0.97f), BorderColor = new Color(0.85f, 0.55f, 0.12f) });
        _panelLayer.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 14);
        margin.AddChild(box);
        panel.AddChild(margin);

        box.AddChild(new Label { Text = "KÜTÜK İSTASYONU", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new TextureRect
        {
            Texture = _preview.GetTexture(),
            CustomMinimumSize = new Vector2(0, 250),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });

        var shapeSelect = new OptionButton();
        shapeSelect.AddItem("Blok");
        shapeSelect.AddItem("Yuvarlak çubuk");
        shapeSelect.ItemSelected += index => { _shape = index == 0 ? StockShape.Block : StockShape.CylinderAlongX; ApplyLimits(); };
        box.AddChild(Row("Şekil", shapeSelect));

        var materialSelect = new OptionButton();
        foreach (var kind in new[] { MaterialKind.Aluminum, MaterialKind.Steel, MaterialKind.Brass })
            materialSelect.AddItem(StockBlank.NameOf(kind), (int)kind);
        materialSelect.ItemSelected += index => { _material = (MaterialKind)materialSelect.GetItemId((int)index); RefreshPreview(); };
        box.AddChild(Row("Malzeme", materialSelect));

        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            _dimLabels[i] = new Label { CustomMinimumSize = new Vector2(120, 0) };
            _sliders[i] = new HSlider { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Step = 1 };
            _spins[i] = new SpinBox { Step = 0.5, Suffix = "mm", CustomMinimumSize = new Vector2(110, 0) };
            _sliders[i].ValueChanged += value => SetSize(axis, (float)value);
            _spins[i].ValueChanged += value => SetSize(axis, (float)value);
            var row = new HBoxContainer();
            row.AddChild(_dimLabels[i]);
            row.AddChild(_sliders[i]);
            row.AddChild(_spins[i]);
            box.AddChild(row);
            if (i == 2)
                _thirdRow = row;
        }

        var produce = new Button { Text = "ÜRET", CustomMinimumSize = new Vector2(0, 44) };
        produce.Pressed += () => Produce();
        box.AddChild(produce);
        box.AddChild(new Label { Text = "[E] kapat", HorizontalAlignment = HorizontalAlignment.Center });
        ApplyLimits();
    }

    private static Control Row(string label, Control field)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(field);
        return row;
    }

    private void ApplyLimits()
    {
        bool block = _shape == StockShape.Block;
        var names = block ? new[] { "X boyu", "Y eni", "Z yüksekliği" } : new[] { "Boy", "Çap", "" };
        var max = block ? new[] { 400f, 300f, 200f } : new[] { 400f, 200f, 200f };
        var min = block ? new[] { 20f, 20f, 10f } : new[] { 20f, 10f, 10f };
        _thirdRow.Visible = block;
        for (int i = 0; i < 3; i++)
        {
            _dimLabels[i].Text = names[i];
            // Changing a range clamps the value and would report that as an edit.
            _sliders[i].SetBlockSignals(true);
            _spins[i].SetBlockSignals(true);
            _sizeMm[i] = Mathf.Clamp(_sizeMm[i], min[i], max[i]);
            _sliders[i].MinValue = _spins[i].MinValue = min[i];
            _sliders[i].MaxValue = _spins[i].MaxValue = max[i];
            _sliders[i].Value = _spins[i].Value = _sizeMm[i];
            _sliders[i].SetBlockSignals(false);
            _spins[i].SetBlockSignals(false);
        }
        RefreshPreview();
    }

    private void SetSize(int axis, float value)
    {
        _sizeMm[axis] = value;
        _sliders[axis].SetValueNoSignal(value);
        _spins[axis].SetValueNoSignal(value);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var blank = Blank;
        BodyParts.Clear(_previewPivot);
        _previewPivot.AddChild(new MeshInstance3D { Mesh = StockItem.BuildMesh(blank), MaterialOverride = StockItem.BuildMaterial(blank.Material) });

        var half = blank.BoundsM / 2f;
        var line = BodyParts.Mat(new Color(1f, 0.85f, 0.2f), 0f, 1f);
        line.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        float thickness = half.Length() * 0.012f, gap = half.Length() * 0.12f;
        void Dimension(Vector3 from, Vector3 to, string text)
        {
            BodyParts.Cylinder(_previewPivot, "DimLine", thickness, from.DistanceTo(to), (from + to) / 2f, to - from, line);
            _previewPivot.AddChild(new Label3D
            {
                Text = text,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0011f,
                FontSize = 44,
                Modulate = new Color(1f, 0.9f, 0.3f),
                Position = (from + to) / 2f + (from + to).Normalized() * gap * 0.6f,
            });
        }
        if (blank.Shape == StockShape.Block)
        {
            Dimension(new Vector3(-half.X, -half.Y, half.Z + gap), new Vector3(half.X, -half.Y, half.Z + gap), $"X {blank.SizeMm.X:0.#}");
            Dimension(new Vector3(half.X + gap, -half.Y, -half.Z), new Vector3(half.X + gap, -half.Y, half.Z), $"Y {blank.SizeMm.Y:0.#}");
            Dimension(new Vector3(half.X + gap, -half.Y, half.Z + gap), new Vector3(half.X + gap, half.Y, half.Z + gap), $"Z {blank.SizeMm.Z:0.#}");
        }
        else
        {
            Dimension(new Vector3(-half.X, -half.Y - gap, 0f), new Vector3(half.X, -half.Y - gap, 0f), $"Boy {blank.LengthMm:0.#}");
            Dimension(new Vector3(half.X + gap, -half.Y, 0f), new Vector3(half.X + gap, half.Y, 0f), $"Ø {blank.DiameterMm:0.#}");
        }

        float distance = half.Length() * 5.2f;
        var offset = new Vector3(0f, 0.45f, 1f).Normalized() * distance;
        _previewCamera.Position = offset;
        _previewCamera.Basis = Basis.LookingAt(-offset, Vector3.Up);
        _previewCamera.Near = distance * 0.05f;
        _previewText.Text = $"{StockBlank.NameOf(blank.Material)}   {blank.Describe()}\n" +
                            $"Hacim {blank.VolumeMm3 / 1000f:0.#} cm³   Kütle {blank.MassKg:0.00} kg";
    }

    public override void _Process(double delta)
    {
        if (_previewPivot != null)
            _previewPivot.RotateY((float)delta * 0.6f);
    }

    public void SetPanelOpen(bool open)
    {
        _panelLayer.Visible = open;
    }

    /// <summary>Drops the configured stock onto the output tray.</summary>
    public StockItem Produce()
    {
        var item = StockItem.Create(Blank);
        GetParent().AddChild(item);
        item.GlobalPosition = GlobalTransform * (TrayCenter + new Vector3(0f, item.Blank.BoundsM.Y / 2f + 0.05f, 0f));
        return item;
    }
}
