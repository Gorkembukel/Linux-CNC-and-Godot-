using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>A whole chuck the player carries between the rack and the
/// spindle nose. Clicking the empty chuck with it (key out) swaps it for
/// the one that was on.</summary>
public partial class ChuckItem : Carryable
{
    public ChuckKind Kind { get; private set; }

    public static string NameOf(ChuckKind kind) => kind switch
    {
        ChuckKind.FourJawIndependent => "4 ayaklı bağımsız ayna",
        ChuckKind.Collet => "Pens aynası",
        _ => "3 ayaklı ayna",
    };

    public static ChuckItem Create(ChuckKind kind)
    {
        var item = new ChuckItem { Kind = kind, Name = "Chuck", DisplayName = NameOf(kind), Mass = kind == ChuckKind.Collet ? 6f : 12f };
        float radius = kind == ChuckKind.Collet ? 0.045f : 0.07f;
        BodyParts.Cylinder(item, "Body", radius, 0.06f, new Vector3(0f, 0.03f, 0f), Vector3.Up, BodyParts.Steel);
        int jaws = kind switch { ChuckKind.FourJawIndependent => 4, ChuckKind.Collet => 0, _ => 3 };
        for (int i = 0; i < jaws; i++)
        {
            float angle = Mathf.Tau * i / jaws;
            BodyParts.Box(item, "Jaw", new Vector3(0.02f, 0.02f, 0.03f),
                new Vector3(Mathf.Cos(angle) * radius * 0.6f, 0.07f, Mathf.Sin(angle) * radius * 0.6f), BodyParts.DarkSteel);
        }
        if (kind == ChuckKind.Collet)
            BodyParts.Cylinder(item, "Nose", 0.025f, 0.03f, new Vector3(0f, 0.075f, 0f), Vector3.Up, BodyParts.DarkSteel, topRadius: 0.018f);
        item.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = 0.08f }, Position = new Vector3(0f, 0.04f, 0f) });
        return item;
    }
}
