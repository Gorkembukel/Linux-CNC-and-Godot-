using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>One chuck jaw, stamped with its number: on a scroll chuck jaw 1
/// goes in slot 1, and so on, each as the scroll's thread start reaches
/// its slot. Made for one kind of chuck.</summary>
public partial class JawItem : Carryable
{
    public JawKind Kind { get; private set; }
    public int Number { get; private set; }
    public ChuckKind MadeFor { get; private set; }

    public static string NameOf(JawKind kind) => kind switch
    {
        JawKind.Hard => "Sert çene",
        JawKind.HardReversed => "Ters sert çene",
        _ => "Yumuşak çene",
    };

    public static string SetNameOf(JawKind kind) => kind switch
    {
        JawKind.Hard => "Sert çeneler",
        JawKind.HardReversed => "Ters takılan sert çeneler",
        _ => "Yumuşak çeneler",
    };

    public static Color ColorOf(JawKind kind) => kind == JawKind.Soft ? new Color(0.78f, 0.79f, 0.82f) : new Color(0.2f, 0.2f, 0.22f);

    public static JawItem Create(JawKind kind, int number, ChuckKind madeFor)
    {
        var item = new JawItem
        {
            Kind = kind, Number = number, MadeFor = madeFor, Name = $"Jaw{number}",
            DisplayName = $"{NameOf(kind)} {number}" + (madeFor == ChuckKind.FourJawIndependent ? " (4 ayaklı ayna)" : ""),
            Mass = 0.5f,
        };
        BodyParts.Box(item, "Jaw", new Vector3(0.025f, 0.03f, 0.05f), new Vector3(0f, 0.015f, 0f),
            BodyParts.Mat(ColorOf(kind), kind == JawKind.Soft ? 0.5f : 0.7f, 0.4f));
        item.AddChild(new Label3D
        {
            Text = number.ToString(), FontSize = 48, PixelSize = 0.0004f,
            Position = new Vector3(0f, 0.031f, 0f), RotationDegrees = new Vector3(-90f, 0f, 0f),
            Modulate = kind == JawKind.Soft ? Colors.Black : Colors.White,
        });
        item.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.025f, 0.03f, 0.05f) }, Position = new Vector3(0f, 0.015f, 0f) });
        return item;
    }
}
