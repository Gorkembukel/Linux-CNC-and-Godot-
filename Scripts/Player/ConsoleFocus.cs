using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Player;

/// <summary>
/// Close-up on a console: the camera flies in front of the panel so it fills
/// the screen, the mouse is free, and clicks / wheel act on what is under
/// the cursor. The wheel off a dial dollies along the cursor ray (the point
/// under the cursor stays put), so small legends become readable without
/// being drawn larger; right-drag pans.
/// </summary>
public partial class ConsoleFocus : Node
{
    [Export] public float Fov = 40f;
    [Export] public float MinDistance = 0.07f;

    public bool Active { get; private set; }
    public PlayerController Player;

    private Camera3D _camera;
    private Camera3D _returnCamera;
    private ConsoleStand _console;
    private Transform3D _base;
    private bool _panning, _leaving;

    public override void _Ready()
    {
        _camera = new Camera3D { Name = "FocusCamera", TopLevel = true, Current = false, Near = 0.01f };
        AddChild(_camera);
    }

    public void Enter(ConsoleStand console, Camera3D from)
    {
        _console = console;
        _returnCamera = from;
        Active = true;
        _leaving = false;
        var size = GetViewport().GetVisibleRect().Size;
        _base = console.FocusTransform(Fov, size.X / size.Y);
        _camera.GlobalTransform = from.GlobalTransform;
        _camera.Fov = from.Fov;
        _camera.Current = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_camera, "global_transform", _base, 0.4);
        tween.TweenProperty(_camera, "fov", Fov, 0.4);
    }

    public void Exit()
    {
        if (_leaving)
            return;
        _leaving = true;
        _panning = false;
        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_camera, "global_transform", _returnCamera.GlobalTransform, 0.35);
        tween.TweenProperty(_camera, "fov", _returnCamera.Fov, 0.35);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _returnCamera.Current = true;
            Active = false;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }));
    }

    public override void _Input(InputEvent @event)
    {
        if (!Active || _leaving)
            return;
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab or Key.Escape }:
                Exit();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                var hit = Pick(click.Position);
                if (IInteractable.Find(hit) is IInteractable target)
                    target.Use(Player);
                else if (hit != null)
                    _console.TryPress(hit);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                int steps = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (IInteractable.Find(Pick(wheel.Position)) is IInteractable dial)
                    dial.Turn(Player, steps);
                else
                    Zoom(steps, wheel.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                _panning = right.Pressed;
                break;
            case InputEventMouseMotion motion when _panning:
                Pan(motion.Relative);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private Node3D Pick(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var query = PhysicsRayQueryParameters3D.Create(from, from + _camera.ProjectRayNormal(screen) * 3f);
        var result = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return result.Count == 0 ? null : result["collider"].As<Node3D>();
    }

    /// <summary>Dollies toward the panel point under the cursor (kept on
    /// the panel, so pointing beside it never steers the camera off the side);
    /// zooming out returns toward the full-panel pose.</summary>
    private void Zoom(int steps, Vector2 screen)
    {
        var origin = _camera.GlobalPosition;
        if (steps < 0)
        {
            _camera.GlobalPosition = origin.Lerp(_base.Origin, 0.3f);
            return;
        }
        var face = _console.FaceTransform;
        var normal = face.Basis.Z.Normalized();
        var dir = _camera.ProjectRayNormal(screen);
        float distance = (origin - face.Origin).Dot(normal);
        var onPanel = face.AffineInverse() * (origin + dir * (distance / Mathf.Max(-dir.Dot(normal), 0.2f)));
        onPanel = new Vector3(Mathf.Clamp(onPanel.X, -0.3f, 0.3f), Mathf.Clamp(onPanel.Y, -0.3f, 0.3f), 0f);
        var target = face * onPanel;
        float fraction = Mathf.Clamp(0.25f, 0f, Mathf.Max(0f, (distance - MinDistance) / distance));
        _camera.GlobalPosition = origin + (target - origin) * fraction;
    }
    private void Pan(Vector2 relative)
    {
        float distance = (_camera.GlobalPosition - _console.FaceTransform.Origin).Dot(_base.Basis.Z);
        float metersPerPixel = 2f * distance * Mathf.Tan(Mathf.DegToRad(Fov / 2f)) / GetViewport().GetVisibleRect().Size.Y;
        var offset = (-_base.Basis.X * relative.X + _base.Basis.Y * relative.Y) * metersPerPixel;
        var local = _base.AffineInverse() * (_camera.GlobalPosition + offset);
        local.X = Mathf.Clamp(local.X, -0.32f, 0.32f);
        local.Y = Mathf.Clamp(local.Y, -0.32f, 0.32f);
        _camera.GlobalPosition = _base * local;
    }
}
