using Godot;

namespace CncTrainer;

/// <summary>
/// Simple orbit camera: hold the right mouse button and drag to orbit around a
/// fixed pivot near the workpiece, scroll to zoom. Added because the fixed
/// starting angle made it impossible to actually see the cut/chips up close.
/// </summary>
public partial class CameraController : Camera3D
{
    [Export] public Vector3 Pivot = new(-40f, 15f, 0f);
    [Export] public float InitialDistance = 320f;
    [Export] public float MinDistance = 30f;
    [Export] public float MaxDistance = 1200f;
    [Export] public float OrbitSpeed = 0.01f;
    [Export] public float ZoomSpeed = 0.1f;

    private float _yaw = -0.3f;
    private float _pitch = 0.5f;
    private float _distance;
    private bool _orbiting;

    public override void _Ready()
    {
        _distance = InitialDistance;
        UpdateTransform();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _orbiting = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp)
                Zoom(-1f);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                Zoom(1f);
        }
        else if (@event is InputEventMouseMotion mm && _orbiting)
        {
            _yaw -= mm.Relative.X * OrbitSpeed;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * OrbitSpeed, -1.4f, 1.4f);
            UpdateTransform();
        }
    }

    private void Zoom(float direction)
    {
        _distance = Mathf.Clamp(_distance * (1f + direction * ZoomSpeed), MinDistance, MaxDistance);
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        var offset = new Vector3(
            Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            Mathf.Sin(_pitch),
            Mathf.Cos(_pitch) * Mathf.Cos(_yaw)
        ) * _distance;
        Position = Pivot + offset;
        LookAt(Pivot, Vector3.Up);
    }
}
