using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Player;

/// <summary>
/// Live picture of the nearby console in a screen corner, so the main view
/// can stay on the part. It renders the real 3D panel (lamps, pressed keys,
/// screen) from a second camera; with the cursor free it is clickable like
/// the panel itself.
/// </summary>
public partial class ConsolePip : TextureRect
{
    [Export] public float HeightFraction = 0.4f;
    [Export] public float Fov = 30f;
    [Export] public int Resolution = 640;

    public PlayerController Player;

    private SubViewport _viewport;
    private Camera3D _camera;
    private ConsoleStand _console;

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Resolution, Resolution),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Fov = Fov, Near = 0.01f, Far = 3f, Current = true };
        _viewport.AddChild(_camera);
        Texture = _viewport.GetTexture();
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = AnchorRight = AnchorTop = AnchorBottom = 1f;
        Visible = false;
    }

    /// <summary>The console to show, or null to hide.</summary>
    public void Show(ConsoleStand console)
    {
        _console = console;
        Visible = console != null;
        _viewport.RenderTargetUpdateMode = Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!Visible)
            return;
        float side = GetViewportRect().Size.Y * HeightFraction;
        OffsetLeft = OffsetTop = -side - 16f;
        OffsetRight = OffsetBottom = -16f;
        _camera.GlobalTransform = console.FocusTransform(Fov, 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_console == null || @event is not InputEventMouseButton { Pressed: true } button)
            return;
        var hit = Pick(button.Position);
        var target = IInteractable.Find(hit);
        switch (button.ButtonIndex)
        {
            case MouseButton.Left when target != null:
                target.Use(Player);
                break;
            case MouseButton.Left:
                _console.TryPress(hit);
                break;
            case MouseButton.WheelUp or MouseButton.WheelDown:
                target?.Turn(Player, button.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    private Node3D Pick(Vector2 local)
    {
        var point = local / Size * _viewport.Size;
        var from = _camera.ProjectRayOrigin(point);
        var query = PhysicsRayQueryParameters3D.Create(from, from + _camera.ProjectRayNormal(point) * 3f);
        var result = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return result.Count == 0 ? null : result["collider"].As<Node3D>();
    }
}
