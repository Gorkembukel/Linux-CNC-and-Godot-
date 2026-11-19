using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>A set of three chuck jaws the player carries between the jaw
/// rack and the chuck. Clicking the chuck with it (key in, chuck empty)
/// swaps it for the set that was on.</summary>
public partial class JawSetItem : Carryable
{
    public JawKind Kind { get; private set; }

    public static string NameOf(JawKind kind) => kind switch
    {
        JawKind.Hard => "Sert çeneler",
        JawKind.HardReversed => "Ters takılacak sert çeneler",
        _ => "Yumuşak çeneler",
    };

    public static Color ColorOf(JawKind kind) => kind == JawKind.Soft ? new Color(0.78f, 0.79f, 0.82f) : new Color(0.2f, 0.2f, 0.22f);

    public static JawSetItem Create(JawKind kind)
    {
        var item = new JawSetItem { Kind = kind, Name = "Jaws", DisplayName = NameOf(kind), Mass = 1.5f };
        var material = BodyParts.Mat(ColorOf(kind), kind == JawKind.Soft ? 0.5f : 0.7f, 0.4f);
        for (int i = 0; i < 3; i++)
            BodyParts.Box(item, "Jaw", new Vector3(0.025f, 0.03f, 0.05f), new Vector3((i - 1) * 0.035f, 0.015f, 0f), material);
        item.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.1f, 0.03f, 0.05f) }, Position = new Vector3(0f, 0.015f, 0f) });
        return item;
    }
}
