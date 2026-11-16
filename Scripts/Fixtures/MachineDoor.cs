using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>Sliding door of an enclosed machine. Closed, nothing behind it
/// can be reached; open, the machine must not run. It locks itself while
/// the spindle turns or a program runs.</summary>
public partial class MachineDoor : StaticBody3D, IInteractable
{
    public bool IsOpen { get; private set; } = true;
    /// <summary>Asked before the door moves: is the machine running?</summary>
    public System.Func<bool> Locked;

    private Vector3 _closedAt;
    private float _slide;

    /// <summary>A door filling an opening of this size centred here (the
    /// parent's frame); it slides along X to open. Starts open.</summary>
    public static MachineDoor Create(Vector3 centre, Vector2 size)
    {
        var door = new MachineDoor { Name = "Door", _closedAt = centre, _slide = size.X * 0.95f };
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.8f, 0.9f, 0.3f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.1f,
        };
        BodyParts.Box(door, "Glass", new Vector3(size.X, size.Y, 0.01f), Vector3.Zero, glass);
        BodyParts.Box(door, "Handle", new Vector3(0.03f, 0.2f, 0.04f), new Vector3(-size.X / 2f + 0.08f, 0f, 0.02f), BodyParts.DarkSteel);
        door.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(size.X, size.Y, 0.03f) } });
        door.Place();
        return door;
    }

    private void Place() => Position = _closedAt + (IsOpen ? new Vector3(_slide, 0f, 0f) : Vector3.Zero);

    public void SetOpen(bool open)
    {
        IsOpen = open;
        Place();
    }

    public string Prompt(PlayerController player) =>
        Locked?.Invoke() == true ? "Kapı kilitli: makine çalışıyor" : IsOpen ? "[Tık] Kapıyı kapat" : "[Tık] Kapıyı aç";

    public void Use(PlayerController player)
    {
        if (Locked?.Invoke() != true)
            SetOpen(!IsOpen);
    }
}
