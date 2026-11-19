using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Fixtures;

/// <summary>One chuck jaw, stamped with its number, made for one kind and
/// size of chuck: on a scroll chuck jaw 1 goes in slot 1, and so on, each
/// as the scroll's thread start reaches its slot.</summary>
public partial class JawItem : Carryable
{
    public JawKind Kind { get; private set; }
    public int Number { get; private set; }
    public ChuckKind MadeFor { get; private set; }
    /// <summary>The chuck size and kind it fits (<see cref="ChuckModel.JawFamily"/>).</summary>
    public string Family { get; private set; } = "";
    /// <summary>Its length along the groove (m), for laying it into one.</summary>
    public float LengthM { get; private set; }

    public bool Fits(ChuckModel model) => model.Kind == MadeFor && (Family == "" || Family == model.JawFamily);

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

    /// <summary>Soft jaws bright aluminium-grey, hardened ones blued steel.</summary>
    public static Color ColorOf(JawKind kind) => kind == JawKind.Soft ? new Color(0.82f, 0.83f, 0.85f) : new Color(0.3f, 0.32f, 0.36f);

    /// <summary>A jaw for this chuck, sized to it.</summary>
    public static JawItem Create(JawKind kind, int number, ChuckModel model) =>
        Create(kind, number, model.Kind, model.JawFamily, model.DiameterMm, model.JawDepthMm, model.DisplayName);

    public static JawItem Create(JawKind kind, int number, ChuckKind madeFor, string family = "", float chuckDiameterMm = 100f,
        float jawDepthMm = 25f, string chuckName = "")
    {
        float r = chuckDiameterMm / 2f * WorldScale.MmToM;
        float length = r * 0.45f, height = jawDepthMm * 0.7f * WorldScale.MmToM, width = r * 0.24f;
        var item = new JawItem
        {
            Kind = kind, Number = number, MadeFor = madeFor, Family = family, LengthM = length, Name = $"Jaw{number}",
            DisplayName = $"{NameOf(kind)} {number}" + (chuckName != "" ? $" ({chuckName})" : ""),
            // Steel, a little under the solid block it fills.
            Mass = Mathf.Max(0.1f, length * height * width * (kind == JawKind.Soft ? 2700f : 7850f) * 0.8f),
        };
        JawMesh.Build(item, kind, number, length, height, width);
        item.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(length, height + 0.004f, width) }, Position = new Vector3(length / 2f, height / 2f - 0.002f, 0f) });
        return item;
    }
}
