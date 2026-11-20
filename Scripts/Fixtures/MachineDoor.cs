using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>What keeps chips and hands out of the cut: the sliding door of
/// an enclosed machine, or the hinged clear guard over an open machine's
/// chuck or cutter. Closed, nothing behind it can be reached; open, the
/// machine must not run. With an interlock it locks itself while the
/// spindle turns or a program runs.</summary>
public partial class MachineDoor : StaticBody3D, IInteractable
{
    public bool IsOpen { get; private set; } = true;
    /// <summary>Asked before the door moves: is the machine running?</summary>
    public System.Func<bool> Locked;
    /// <summary>What the prompts call it.</summary>
    public string Noun { get; private set; } = "Kapı";

    private Transform3D _closed, _open;

    private static StandardMaterial3D Clear => new()
    {
        AlbedoColor = new Color(0.6f, 0.8f, 0.9f, 0.3f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };

    /// <summary>A door filling an opening of this size centred here (the
    /// parent's frame); it slides along X to open. Starts open.</summary>
    public static MachineDoor Create(Vector3 centre, Vector2 size)
    {
        var door = new MachineDoor
        {
            Name = "Door",
            _closed = new Transform3D(Basis.Identity, centre),
            _open = new Transform3D(Basis.Identity, centre + new Vector3(size.X * 0.95f, 0f, 0f)),
        };
        BodyParts.Box(door, "Glass", new Vector3(size.X, size.Y, 0.01f), Vector3.Zero, Clear);
        BodyParts.Box(door, "Handle", new Vector3(0.03f, 0.2f, 0.04f), new Vector3(-size.X / 2f + 0.08f, 0f, 0.02f), BodyParts.DarkSteel);
        door.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(size.X, size.Y, 0.03f) } });
        door.Place();
        return door;
    }

    /// <summary>A clear guard hung from a hinge along X (the parent's frame):
    /// its front panel hangs size.Y down, size.Z in front of the hinge
    /// (roofed back to the hinge when `roof`), size.X wide. Swung up about
    /// the hinge to open. Starts open.</summary>
    public static MachineDoor Guard(Vector3 hinge, Vector3 size, bool roof)
    {
        var guard = new MachineDoor
        {
            Name = "Guard", Noun = "Siper",
            _closed = new Transform3D(Basis.Identity, hinge),
            _open = new Transform3D(new Basis(Vector3.Right, -Mathf.DegToRad(110f)), hinge),
        };
        const float sheet = 0.004f;
        void Panel(string name, Vector3 extent, Vector3 centre)
        {
            BodyParts.Box(guard, name, extent, centre, Clear);
            guard.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = extent + Vector3.One * 0.01f }, Position = centre });
        }
        Panel("Front", new Vector3(size.X, size.Y, sheet), new Vector3(0f, -size.Y / 2f, size.Z));
        if (roof)
            Panel("Roof", new Vector3(size.X, sheet, size.Z), new Vector3(0f, 0f, size.Z / 2f));
        foreach (float side in new[] { -1f, 1f })
            Panel("Side", new Vector3(sheet, size.Y, size.Z), new Vector3(side * size.X / 2f, -size.Y / 2f, size.Z / 2f));
        BodyParts.Box(guard, "Handle", new Vector3(0.05f, 0.012f, 0.02f), new Vector3(0f, -size.Y + 0.01f, size.Z + 0.012f), BodyParts.DarkSteel);
        guard.Place();
        return guard;
    }

    private void Place() => Transform = IsOpen ? _open : _closed;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        Place();
    }

    public string Prompt(PlayerController player) =>
        Locked?.Invoke() == true ? $"{Noun} kilitli: makine çalışıyor" : $"[Tık] {Noun} {(IsOpen ? "kapat" : "aç")}";

    public void Use(PlayerController player)
    {
        if (Locked?.Invoke() != true)
            SetOpen(!IsOpen);
    }
}
