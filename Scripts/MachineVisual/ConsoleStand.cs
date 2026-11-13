using Godot;
using CncTrainer.Bridge;

namespace CncTrainer;

/// <summary>
/// A free-standing physical operator console: a post, a tilted panel face
/// with decorative buttons, and a monitor screen that shows live machine data
/// via a SubViewport-rendered texture. User request: "the panel and monitor
/// where these controls would really be done."
///
/// Scope note, stated plainly: this is a VISUAL console -- the screen shows
/// live read-only status (mirroring OperatorPanel's own readout), but it is
/// not itself clickable; the proven, already-working 2D `OperatorPanel`
/// overlay (confirmed live by the user) remains the actual way to send
/// commands. Making the 3D buttons/screen independently interactive (3D
/// raycasting onto UI, or duplicating OperatorPanel's live logic into the
/// SubViewport) was deliberately left out to avoid risking the working
/// control path for a visual-only improvement -- flagged here rather than
/// silently assumed equivalent to a real 3D-interactive panel.
/// </summary>
public partial class ConsoleStand : Node3D
{
    [Export] public Vector3 BasePosition = new(-50f, -45f, 65f);
    [Export] public float PostHeight = 70f;
    [Export] public float PanelTiltDegrees = -25f;

    private BridgeClient _bridge;
    private Label _screenLabel;

    public override void _Ready()
    {
        _bridge = GetNode<BridgeClient>("/root/BridgeClient");

        var consoleBody = Mat(new Color(0.78f, 0.78f, 0.75f), metallic: 0.1f, rough: 0.7f); // off-white pendant plastic
        var darkTrim = Mat(new Color(0.12f, 0.12f, 0.13f), metallic: 0.2f, rough: 0.6f);

        Position = BasePosition;

        // Post: stands the console up from the floor to shoulder height.
        var post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(14f, PostHeight, 14f) },
            MaterialOverride = darkTrim,
            Position = new Vector3(0f, PostHeight / 2f, 0f),
            Name = "Post",
        };
        AddChild(post);

        // Panel: the tilted face carrying the screen and buttons -- its own
        // Node3D so children inherit the tilt without manual trig.
        var panel = new Node3D
        {
            Position = new Vector3(0f, PostHeight + 10f, 0f),
            RotationDegrees = new Vector3(PanelTiltDegrees, 0f, 0f),
            Name = "Panel",
        };
        AddChild(panel);

        panel.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(60f, 45f, 10f) },
            MaterialOverride = consoleBody,
            Name = "PanelBody",
        });

        BuildScreen(panel, consoleBody);
        BuildButtons(panel, darkTrim);
    }

    private void BuildScreen(Node3D panel, Godot.Material bezelMaterial)
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
        viewport.AddChild(_screenLabel);

        var screenMat = new StandardMaterial3D
        {
            AlbedoTexture = viewport.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        panel.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(48f, 30f) },
            MaterialOverride = screenMat,
            Position = new Vector3(0f, 2f, 5.1f),
            Name = "Screen",
        });

        _ = bezelMaterial;
    }

    private void BuildButtons(Node3D panel, Godot.Material buttonMaterial)
    {
        // Purely decorative grid of buttons below the screen.
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 6; col++)
            {
                panel.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 2.2f, BottomRadius = 2.2f, Height = 2f },
                    MaterialOverride = buttonMaterial,
                    RotationDegrees = new Vector3(90f, 0f, 0f),
                    Position = new Vector3(-13f + col * 5.2f, -14f - row * 6f, 6f),
                });
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_screenLabel == null)
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
