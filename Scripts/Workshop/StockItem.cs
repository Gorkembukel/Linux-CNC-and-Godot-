using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Spec;

namespace CncTrainer.Workshop;

/// <summary>A piece of raw stock as a physical, carryable object. Local
/// frame: X along the length, Y up, origin at the centre.</summary>
public partial class StockItem : Carryable
{
    public StockBlank Blank { get; private set; }

    public static StockItem Create(StockBlank blank)
    {
        var item = new StockItem
        {
            Blank = blank,
            Name = "Stock",
            DisplayName = $"{StockBlank.NameOf(blank.Material)} kütük {blank.Describe()}",
            Mass = Mathf.Max(0.05f, blank.MassKg),
        };
        item.AddChild(new MeshInstance3D { Mesh = BuildMesh(blank), MaterialOverride = BuildMaterial(blank.Material) });
        var bounds = blank.BoundsM;
        var shape = new CollisionShape3D();
        if (blank.Shape == StockShape.Block)
            shape.Shape = new BoxShape3D { Size = bounds };
        else
        {
            shape.Shape = new CylinderShape3D { Radius = bounds.Y / 2f, Height = bounds.X };
            shape.RotationDegrees = new Vector3(0f, 0f, 90f);
        }
        item.AddChild(shape);
        return item;
    }

    /// <summary>Mesh in the stock's own frame (see the class comment).</summary>
    public static Mesh BuildMesh(StockBlank blank)
    {
        var bounds = blank.BoundsM;
        if (blank.Shape == StockShape.Block)
            return new BoxMesh { Size = bounds };

        // CylinderMesh runs along Y; bake the turn onto X into the mesh.
        var tool = new SurfaceTool();
        tool.AppendFrom(new CylinderMesh { TopRadius = bounds.Y / 2f, BottomRadius = bounds.Y / 2f, Height = bounds.X, RadialSegments = 48 },
            0, new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), Vector3.Zero));
        return tool.Commit();
    }

    public static StandardMaterial3D BuildMaterial(MaterialKind material) => new()
    {
        AlbedoColor = MaterialPreset.Get(material).BaseColor,
        Metallic = 0.75f,
        Roughness = 0.35f,
    };
}
