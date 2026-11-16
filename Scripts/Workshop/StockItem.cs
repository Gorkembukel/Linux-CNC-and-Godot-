using System.Linq;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Spec;

namespace CncTrainer.Workshop;

/// <summary>A piece of raw stock as a physical, carryable object. Local
/// frame: X along the length, Y up, origin at the centre.</summary>
public partial class StockItem : Carryable, IMeasurable
{
    public StockBlank Blank { get; private set; }

    /// <summary>Held by a fixture: not free to pick up.</summary>
    public bool Fixed;
    public override bool CanPickUp => !Fixed;

    /// <summary>Once machining has started the cut surface is measured;
    /// untouched stock simply has its nominal size.</summary>
    public string Reading(HandToolKind gauge, Vector3 globalPoint) =>
        GetChildren().OfType<VoxelStockVisual>().FirstOrDefault() is VoxelStockVisual machined ? machined.Reading(gauge, globalPoint)
        : gauge == HandToolKind.Caliper ? "Kumpas: " + Blank.Describe()
        : null;

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
