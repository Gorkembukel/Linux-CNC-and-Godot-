using Godot;
using CncTrainer.Bridge;

namespace CncTrainer;

/// <summary>
/// A free-standing physical operator console: a post, a tilted panel face
/// with decorative buttons, and a monitor screen that shows live machine data
/// via a SubViewport-rendered texture. User request: "the panel and monitor
/// where these controls would really be done."
///
/// [Tool]: the post/panel/button geometry builds in the editor too (see
/// MachineBody.cs's doc comment for why). The BridgeClient-dependent screen
/// (live data) only makes sense at play time, so that part is guarded by
/// Engine.IsEditorHint().
///
/// World units are meters (see WorldScale.cs). Post/panel get collision
/// (StaticBody3D) since a future human character (planned: FP/TP controller)
/// needs to not walk through the console.
///
/// The 12 face buttons are REAL and clickable (see TryPress, called by
/// PlayerController's crosshair raycast): they cover the core workflow
/// (machine on/estop/home/cycle-start/feed-hold/stop/tool-change-confirm/jog)
/// directly in 3D. MDI typing and the touch-off text fields still only exist
/// on the 2D OperatorPanel (proximity + E) -- a world-space button can't
/// usefully host a text field, so that boundary is deliberate, not an
/// oversight.
/// </summary>
[Tool]
public partial class ConsoleStand : Node3D
{
    [Export] public Vector3 BasePosition = new(-0.15f, WorldScale.FloorY, 0.5f);
    [Export] public float PostHeight = 0.95f;
    [Export] public float PanelTiltDegrees = -25f;

    private BridgeClient _bridge;
    private Label _screenLabel;
    private readonly System.Collections.Generic.Dictionary<ulong, System.Action> _buttonActions = new();

    public override void _Ready()
    {
        Position = BasePosition;
        RebuildGeometry();

        if (Engine.IsEditorHint())
            return; // the rest is runtime-only (needs the BridgeClient autoload)

        _bridge = GetNode<BridgeClient>("/root/BridgeClient");
    }

    private void RebuildGeometry()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttonActions.Clear();

        var consoleBody = Mat(new Color(0.78f, 0.78f, 0.75f), metallic: 0.1f, rough: 0.7f); // off-white pendant plastic
        var darkTrim = Mat(new Color(0.12f, 0.12f, 0.13f), metallic: 0.2f, rough: 0.6f);

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
            Position = new Vector3(0f, PostHeight + 0.15f, 0f),
            RotationDegrees = new Vector3(PanelTiltDegrees, 0f, 0f),
            Name = "Panel",
        };
        AddChild(panel);

        var panelBody = new StaticBody3D { Name = "PanelBody" };
        panel.AddChild(panelBody);
        var panelSize = new Vector3(0.4f, 0.3f, 0.08f);
        panelBody.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = panelSize }, MaterialOverride = consoleBody });
        panelBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = panelSize } });

        BuildScreen(panel);
        BuildButtons(panel, darkTrim);
    }

    private void BuildScreen(Node3D panel)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(320, 200),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        AddChild(viewport); // the viewport itself doesn't need the panel's tilt

        var bg = new ColorRect { Color = new Color(0.02f, 0.05f, 0.03f), Size = new Vector2(320, 200) };
        viewport.AddChild(bg);
        _screenLabel = new Label
        {
            Position = new Vector2(8, 8),
            Modulate = new Color(0.3f, 1f, 0.4f), // monochrome-CRT green, a deliberate retro-pendant look
        };
        _screenLabel.AddThemeFontSizeOverride("font_size", 16);
        _screenLabel.Text = Engine.IsEditorHint() ? "(live data at runtime)" : "NO SIGNAL";
        viewport.AddChild(_screenLabel);

        var screenMat = new StandardMaterial3D
        {
            AlbedoTexture = viewport.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        panel.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.32f, 0.2f) },
            MaterialOverride = screenMat,
            Position = new Vector3(0f, 0.015f, 0.041f),
            Name = "Screen",
        });
    }

    private void BuildButtons(Node3D panel, Godot.Material buttonMaterial)
    {
        // Real, clickable buttons (see TryPress) covering the core workflow.
        // MDI/touch-off need text entry, which stays on the 2D panel only.
        (string label, System.Action action)[] buttons =
        {
            ("ON", () => { _bridge.ResetEstop(); _bridge.MachineOn(); }),
            ("ESTOP", () => _bridge.MachineOff()),
            ("HOME", () => _bridge.HomeAxis(0xFF)),
            ("CYCLE", () => _bridge.RunProgram()),
            ("HOLD", () => _bridge.PauseProgram()),
            ("STOP", () => _bridge.StopProgram()),
            ("T.CHG", () => _bridge.ConfirmToolChange()),
            ("X-", () => _bridge.JogIncremental(0, -10f, -1f)),
            ("X+", () => _bridge.JogIncremental(0, 10f, 1f)),
            ("Z-", () => _bridge.JogIncremental(1, -10f, -1f)),
            ("Z+", () => _bridge.JogIncremental(1, 10f, 1f)),
            ("RUN", () =>
            {
                _bridge.OpenProgram("nc_files/demo_facing_turning.ngc");
                _bridge.RunProgram();
            }),
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            int row = i / 6, col = i % 6;
            var pos = new Vector3(-0.09f + col * 0.035f, -0.095f - row * 0.04f, 0.045f);

            var body = new StaticBody3D { InputRayPickable = true };
            panel.AddChild(body);
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.015f },
                MaterialOverride = buttonMaterial,
                RotationDegrees = new Vector3(90f, 0f, 0f),
                Position = pos,
            });
            body.AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = 0.016f, Height = 0.02f },
                RotationDegrees = new Vector3(90f, 0f, 0f),
                Position = pos,
            });
            body.AddChild(new Label3D
            {
                Text = buttons[i].label,
                Position = pos + new Vector3(0f, 0.014f, 0.01f),
                FontSize = 10,
                PixelSize = 0.001f,
                Modulate = new Color(0.1f, 0.1f, 0.1f),
                NoDepthTest = true,
            });

            if (Engine.IsEditorHint())
                continue; // action closures capture _bridge, which is null in the editor
            _buttonActions[body.GetInstanceId()] = buttons[i].action;
        }
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
        if (_screenLabel == null || _bridge == null)
            return;
        var s = _bridge.State;
        _screenLabel.Text = _bridge.IsBridgeConnected
            ? $"X {s.Position.X,8:F3}\nZ {s.Position.Y,8:F3}\n" +
              $"FEED {s.FeedRate,6:F1}\nSPDL {s.SpindleRpm,6:F0}\n" +
              $"TOOL T{s.ToolNumber}\n{s.TaskMode}  {(s.Estop ? "ESTOP" : s.MachineOn ? "ON" : "OFF")}"
            : "NO SIGNAL";
    }

    private static StandardMaterial3D Mat(Color color, float metallic, float rough) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = rough,
    };
}
