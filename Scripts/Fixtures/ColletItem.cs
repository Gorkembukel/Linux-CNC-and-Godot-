using Godot;
using CncTrainer.Interaction;
using CncTrainer.Procedural;

namespace CncTrainer.Fixtures;

/// <summary>A collet for a collet chuck: holds bars from its size down to
/// 1 mm under it.</summary>
public partial class ColletItem : Carryable, Save.ISavedItem
{
    public Godot.Collections.Dictionary SaveState() => new() { ["size"] = SizeMm };

    public static ColletItem Restore(Godot.Collections.Dictionary d) => Create(d["size"].AsSingle());

    public float SizeMm { get; private set; }

    public static ColletItem Create(float sizeMm)
    {
        var item = new ColletItem
        {
            SizeMm = sizeMm, Name = "Collet",
            DisplayName = System.FormattableString.Invariant($"Pens Ø{sizeMm:0} ({sizeMm - 1f:0}-{sizeMm:0} mm)"),
            Mass = 0.15f,
        };
        BodyParts.Cylinder(item, "Collet", 0.017f, 0.04f, new Vector3(0f, 0.02f, 0f), Vector3.Up, BodyParts.Steel, topRadius: 0.0135f);
        item.AddChild(new Label3D
        {
            Text = System.FormattableString.Invariant($"{sizeMm:0}"), FontSize = 40, PixelSize = 0.0004f,
            Position = new Vector3(0f, 0.041f, 0f), RotationDegrees = new Vector3(-90f, 0f, 0f), Modulate = Colors.Black,
        });
        item.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.017f, Height = 0.04f }, Position = new Vector3(0f, 0.02f, 0f) });
        return item;
    }
}
