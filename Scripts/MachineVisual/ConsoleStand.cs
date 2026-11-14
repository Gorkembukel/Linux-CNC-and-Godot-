using Godot;
using CncTrainer.Bridge;

namespace CncTrainer;

/// <summary>
/// A free-standing physical operator console: a post, a tilted panel face
/// with real clickable buttons, and a multi-page monitor showing live
/// LinuxCNC data via a SubViewport-rendered texture. User request: "a real
/// CNC machine look, with the monitor showing LinuxCNC's own data across
/// multiple pages, like a real control."
///
/// [Tool]: the post/panel/button geometry builds in the editor too (see
/// MachineBody.cs's doc comment for why). The BridgeClient-dependent screen
/// (live data) only makes sense at play time, so that part is guarded by
/// Engine.IsEditorHint().
///
/// World units are meters (see WorldScale.cs). Post/panel get collision
/// (StaticBody3D) since a human character needs to not walk through the
/// console.
///
/// All face buttons (including PAGE </>) are real and clickable (see
/// TryPress, called by PlayerController's crosshair raycast). MDI typing and
/// the touch-off text fields still only exist on the 2D OperatorPanel
/// (proximity + E) -- a world-space button can't usefully host a text field,
/// so that boundary is deliberate.
/// </summary>
[Tool]
public partial class ConsoleStand : Node3D
{
    [Export]
    public float PostHeight
    {
        get => _postHeight;
        set { _postHeight = value; RebuildIfInTree(); }
    }

    [Export]
    public float PanelTiltDegrees
    {
        get => _panelTiltDegrees;
        set { _panelTiltDegrees = value; RebuildIfInTree(); }
    }

    private float _postHeight = 0.95f;
    private float _panelTiltDegrees = -25f;
    private string _demoProgram = "";

    private void RebuildIfInTree()
    {
        if (IsInsideTree())
            RebuildGeometry();
    }

    private const int ScreenPixelWidth = 420;
    private const int ScreenPixelHeight = 252;

    private static readonly string[] PageNames = { "POSITION", "PROGRAM", "TOOL / OFFSET", "STATUS / ALARM" };

    private BridgeClient _bridge;
    // Placeholder until Configure() runs (editor preview + the first runtime
    // frame); matches a lathe's real axes, same as LatheMachine's own.
    private (AxisId Axis, byte Joint)[] _axes = { (AxisId.X, (byte)0), (AxisId.Z, (byte)1) };
    private Label _titleLabel;
    private Label _bodyLabel;
    private int _page;
    private readonly System.Collections.Generic.Dictionary<ulong, System.Action> _buttonActions = new();

    private StandardMaterial3D _ledPowerMat, _ledCycleMat, _ledAlarmMat;

    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
            AddToGroup("console_stands");
        RebuildGeometry(); // editor preview + runtime's first frame, using the placeholder axes above
    }

    /// <summary>Called by Main._Ready once this rig's BridgeClient exists --
    /// replaces the old self-lookup via the fixed "/root/BridgeClient"
    /// autoload path. Rebuilds so the physical jog buttons always match
    /// whichever rig this console is actually wired to, not just the
    /// placeholder default.</summary>
    public void Configure(MachineRig rig)
    {
        _bridge = rig.Bridge;
        _axes = rig.Profile.Axes;
        _demoProgram = rig.Profile.DemoProgram;
        RebuildGeometry();
    }

    private void RebuildGeometry()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttonActions.Clear();

        var consoleBody = Mat(new Color(0.85f, 0.84f, 0.78f), metallic: 0.1f, rough: 0.7f); // off-white/cream pendant plastic
        var darkTrim = Mat(new Color(0.12f, 0.12f, 0.13f), metallic: 0.2f, rough: 0.6f);
        var redEstop = Mat(new Color(0.75f, 0.05f, 0.03f), metallic: 0.3f, rough: 0.4f);

        // Post: stands the console up from the floor to shoulder height. Has
        // collision (a human shouldn't walk through it).
        var post = new StaticBody3D { Name = "Post", Position = new Vector3(0f, PostHeight / 2f, 0f) };
        AddChild(post);
        post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, PostHeight, 0.1f) }, MaterialOverride = darkTrim });
        post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.1f, PostHeight, 0.1f) } });

        // Panel: the tilted face carrying the screen and buttons -- its own
        // Node3D so children inherit the tilt without manual trig. Also has
        // collision (StaticBody3D handles an ancestor rotation-only transform
        // correctly; there's no scale involved, which is the usual Godot
        // physics gotcha with transformed collision shapes).
        var panel = new Node3D
        {
            Position = new Vector3(0f, PostHeight + 0.2f, 0f),
            RotationDegrees = new Vector3(PanelTiltDegrees, 0f, 0f),
            Name = "Panel",
        };
        AddChild(panel);

        var panelBody = new StaticBody3D { Name = "PanelBody" };
        panel.AddChild(panelBody);
        var panelSize = new Vector3(0.52f, 0.46f, 0.08f);
        panelBody.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = panelSize }, MaterialOverride = consoleBody });
        panelBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = panelSize } });

        BuildScreen(panel);
        BuildStatusLeds(panel);
        BuildPageButtons(panel, darkTrim);
        BuildControlButtons(panel, darkTrim, redEstop);
    }

    /// <summary>Real CNC panels put status LEDs where you can see them without
    /// reading the screen -- power/running/alarm at a glance. Dim when off,
    /// lit (emissive) when the corresponding state is true; updated in
    /// _Process alongside the screen text.</summary>
    private void BuildStatusLeds(Node3D panel)
    {
        (string label, Color color, System.Action<StandardMaterial3D> store)[] leds =
        {
            ("POWER", new Color(0.2f, 1f, 0.3f), m => _ledPowerMat = m),
            ("CYCLE", new Color(1f, 0.75f, 0.1f), m => _ledCycleMat = m),
            ("ALARM", new Color(1f, 0.15f, 0.1f), m => _ledAlarmMat = m),
        };

        for (int i = 0; i < leds.Length; i++)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = leds[i].color * 0.25f, // dim = off
                EmissionEnabled = true,
                Emission = leds[i].color,
                EmissionEnergyMultiplier = 0.1f,
            };
            leds[i].store(mat);

            var pos = new Vector3(-0.16f + i * 0.08f, 0.205f, 0.045f);
            panel.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.01f }, MaterialOverride = mat, Position = pos });
            AddButtonCaption(panel, leds[i].label, pos + new Vector3(0f, 0.016f, -0.005f));
        }
    }

    private void BuildScreen(Node3D panel)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(ScreenPixelWidth, ScreenPixelHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        AddChild(viewport); // the viewport itself doesn't need the panel's tilt

        var bg = new ColorRect { Color = new Color(0.02f, 0.05f, 0.03f), Size = new Vector2(ScreenPixelWidth, ScreenPixelHeight) };
        viewport.AddChild(bg);

        var screenGreen = new Color(0.3f, 1f, 0.4f); // monochrome-CRT green, a deliberate retro-pendant look
        _titleLabel = new Label
        {
            Position = new Vector2(6, 4),
            Size = new Vector2(ScreenPixelWidth - 12, 24),
            Modulate = screenGreen,
            Text = "POSITION               1/4",
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        viewport.AddChild(_titleLabel);

        viewport.AddChild(new ColorRect { Color = screenGreen, Position = new Vector2(6, 28), Size = new Vector2(ScreenPixelWidth - 12, 1) });

        _bodyLabel = new Label
        {
            Position = new Vector2(6, 36),
            Modulate = screenGreen,
            Text = Engine.IsEditorHint() ? "(live data at runtime)" : "NO SIGNAL",
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", 15);
        viewport.AddChild(_bodyLabel);

        var screenMat = new StandardMaterial3D
        {
            AlbedoTexture = viewport.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        panel.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.4f, 0.24f) },
            MaterialOverride = screenMat,
            Position = new Vector3(0f, 0.07f, 0.041f),
            Name = "Screen",
        });
    }

    /// <summary>Small dedicated PAGE &lt;/&gt; keys right under the screen --
    /// real CNC pendants separate page navigation from the main keypad the
    /// same way.</summary>
    private void BuildPageButtons(Node3D panel, Godot.Material buttonMaterial)
    {
        AddRectButton(panel, "PAGE <", buttonMaterial, new Vector3(-0.1f, -0.1f, 0.045f),
            () => _page = (_page - 1 + PageNames.Length) % PageNames.Length);
        AddRectButton(panel, "PAGE >", buttonMaterial, new Vector3(0.1f, -0.1f, 0.045f),
            () => _page = (_page + 1) % PageNames.Length);
    }

    private void BuildControlButtons(Node3D panel, Godot.Material buttonMaterial, Godot.Material estopMaterial)
    {
        // Real, clickable buttons (see TryPress) covering the core workflow.
        // MDI/touch-off need text entry, which stays on the 2D panel only.
        // Fixed prefix/suffix buttons, with one jog -/+ pair per this rig's
        // actual axis list spliced in between -- the row/col grid below
        // already adapts to whatever length this list ends up, so a
        // machine with more axes (Phase B's mill) needs no layout change.
        var buttons = new System.Collections.Generic.List<(string label, System.Action action)>
        {
            ("ON", () => { _bridge.ResetEstop(); _bridge.MachineOn(); }),
            ("HOME", () => _bridge.HomeAxis(0xFF)),
            ("CYCLE", () => _bridge.RunProgram()),
            ("HOLD", () => _bridge.PauseProgram()),
            ("STOP", () => _bridge.StopProgram()),
            ("T.CHG", () => _bridge.ConfirmToolChange()),
        };
        foreach (var (axis, joint) in _axes)
        {
            buttons.Add(($"{axis}-", () => _bridge.JogIncremental(joint, -10f, -1f)));
            buttons.Add(($"{axis}+", () => _bridge.JogIncremental(joint, 10f, 1f)));
        }
        buttons.Add(("RUN", () =>
        {
            _bridge.OpenProgram(_demoProgram);
            _bridge.RunProgram();
        }));

        for (int i = 0; i < buttons.Count; i++)
        {
            int row = i / 4, col = i % 4;
            var pos = new Vector3(-0.15f + col * 0.1f, -0.14f - row * 0.04f, 0.045f);
            AddRoundButton(panel, buttons[i].label, buttonMaterial, pos, 0.016f, buttons[i].action);
        }

        // E-STOP: real-world convention is a big red mushroom button, visually
        // distinct from everything else and placed prominently -- adds realism
        // and matches the "look like a real machine" brief goal.
        AddRoundButton(panel, "E-STOP", estopMaterial, new Vector3(0.21f, -0.02f, 0.045f), 0.03f,
            () => _bridge.MachineOff());
    }

    private void AddRoundButton(Node3D panel, string label, Godot.Material material, Vector3 pos, float radius, System.Action action)
    {
        var body = new StaticBody3D { InputRayPickable = true };
        panel.AddChild(body);
        body.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.015f },
            MaterialOverride = material,
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Position = pos,
        });
        body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius + 0.001f, Height = 0.02f },
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Position = pos,
        });
        AddButtonCaption(body, label, pos + new Vector3(0f, radius + 0.006f, 0.01f));
        RegisterButton(body, action);
    }

    private void AddRectButton(Node3D panel, string label, Godot.Material material, Vector3 pos, System.Action action)
    {
        var size = new Vector3(0.05f, 0.02f, 0.015f);
        var body = new StaticBody3D { InputRayPickable = true };
        panel.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = pos });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos });
        AddButtonCaption(body, label, pos + new Vector3(0f, 0.017f, 0.009f));
        RegisterButton(body, action);
    }

    /// <summary>Bright, outlined, billboarded caption -- the first version used
    /// small, dark, flat-facing text that was unreadable against the equally
    /// dark button material (reported live: "can't read the button names").
    /// Outline gives contrast against ANY background color, not just one
    /// that happens to be picked correctly, and Billboard keeps it legible
    /// from any approach angle instead of only straight-on.</summary>
    private void AddButtonCaption(Node3D parent, string text, Vector3 position)
    {
        parent.AddChild(new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 24,
            PixelSize = 0.0012f,
            Modulate = new Color(1f, 0.95f, 0.5f), // warm "backlit" amber, reads as illuminated
            OutlineSize = 10,
            OutlineModulate = new Color(0f, 0f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        });
    }

    private void RegisterButton(StaticBody3D body, System.Action action)
    {
        if (Engine.IsEditorHint())
            return; // action closures capture _bridge, which is null in the editor
        _buttonActions[body.GetInstanceId()] = action;
    }

    /// <summary>Called by PlayerController when its crosshair raycast hits a
    /// Node3D within interaction range. Returns true (and runs the mapped
    /// action) if that node is one of this console's buttons.</summary>
    public bool TryPress(Node3D hit)
    {
        if (hit != null && _buttonActions.TryGetValue(hit.GetInstanceId(), out var action))
        {
            action();
            return true;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        if (_bodyLabel == null || _bridge == null)
            return;

        _titleLabel.Text = $"{PageNames[_page],-22}{_page + 1}/{PageNames.Length}";

        if (!_bridge.IsBridgeConnected)
        {
            _bodyLabel.Text = "NO SIGNAL";
            SetLed(_ledPowerMat, false);
            SetLed(_ledCycleMat, false);
            SetLed(_ledAlarmMat, false);
            return;
        }

        var s = _bridge.State;
        SetLed(_ledPowerMat, s.MachineOn);
        SetLed(_ledCycleMat, s.InCycle);
        SetLed(_ledAlarmMat, s.Estop);

        _bodyLabel.Text = _page switch
        {
            0 => // POSITION -- the actual machine coordinates LinuxCNC reports, plus motion state
                AxisLines(s) + "\n" +
                $"FEED   {s.FeedRate,8:F1} ({s.FeedOverride * 100:F0}%)\n" +
                $"SPDL   {s.SpindleRpm,8:F0} ({s.SpindleOverride * 100:F0}%)\n" +
                $"{(s.InCycle ? "IN CYCLE" : "IDLE")}",
            1 => // PROGRAM -- what's loaded/running, matching a real control's program page
                $"MODE   {s.TaskMode}\n" +
                $"INTERP {s.InterpState}\n" +
                $"LINE   {s.MotionLine}\n\n" +
                (s.InterpState == InterpState.Waiting
                    ? "TOOL CHANGE PENDING\nPress T.CHG to confirm"
                    : s.InCycle ? "PROGRAM RUNNING" : "PROGRAM IDLE"),
            2 => // TOOL / OFFSET -- active tool + its nose radius, as reported by LinuxCNC's tool table
                $"ACTIVE TOOL   T{s.ToolNumber}\n" +
                $"NOSE RADIUS   {s.ToolNoseRadius:F3} mm\n\n" +
                $"Use the 2D panel's TOUCH OFF\nworkflow to set tool offsets.",
            _ => // STATUS / ALARM
                $"ESTOP      {(s.Estop ? "*** ACTIVE ***" : "clear")}\n" +
                $"MACHINE    {(s.MachineOn ? "ON" : "OFF")}\n" +
                $"HOMED      {HomedSummary(s)}\n" +
                $"SPINDLE    {(s.SpindleEnabled ? "ON" : "off")}\n\n" +
                $"link: seq={s.LastSeq} age={_bridge.LastPacketAgeSeconds * 1000:F0}ms",
        };
    }

    private string AxisLines(MachineState s)
    {
        string text = "";
        foreach (var (axis, _) in _axes)
            text += $"{axis}  {s.Position.Get(axis),10:F4}\n";
        return text;
    }

    private string HomedSummary(MachineState s)
    {
        string text = "";
        foreach (var (axis, _) in _axes)
            text += $"{axis}:{(s.IsHomed(axis) ? "Y" : "n")} ";
        return text;
    }

    private static void SetLed(StandardMaterial3D mat, bool on) =>
        mat.EmissionEnergyMultiplier = on ? 2.5f : 0.1f;

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };
}
