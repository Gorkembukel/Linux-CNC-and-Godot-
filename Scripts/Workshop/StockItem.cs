using System.Linq;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Material;
using CncTrainer.Spec;

namespace CncTrainer.Workshop;

/// <summary>A piece of stock as a physical, carryable object: raw, or a
/// part already turned (Turned) or milled (a VoxelStockVisual child), ready
/// to go into any fixture again. Local frame: X along the length, Y up,
/// origin at the centre.</summary>
public partial class StockItem : Carryable, IMeasurable
{
    public StockBlank Blank { get; private set; }

    /// <summary>A turned part: outside and bore radii every StepMm from the
    /// -X end to the +X end. Null for raw or milled stock.</summary>
    public (float[] RadiiMm, float[] InnerMm, float StepMm)? Turned { get; private set; }

    /// <summary>Diameter a chuck's jaws close on when the -X end goes in first.</summary>
    public float GripDiameterMm => Turned is { } t ? 2f * t.RadiiMm[0] : Blank.DiameterMm;

    /// <summary>Held by a fixture: not free to pick up.</summary>
    public bool Fixed;
    public override bool CanPickUp => !Fixed;

    /// <summary>Once machining has started the cut surface is measured;
    /// untouched stock simply has its nominal size.</summary>
    public string Reading(HandToolKind gauge, Vector3 globalPoint)
    {
        if (GetChildren().OfType<VoxelStockVisual>().FirstOrDefault() is VoxelStockVisual machined)
            return machined.Reading(gauge, globalPoint);
        if (Turned is { } t)
        {
            var local = ToLocal(globalPoint) / WorldScale.MmToM;
            int index = Mathf.Clamp(Mathf.RoundToInt((local.X + Blank.LengthMm / 2f) / t.StepMm), 0, t.RadiiMm.Length - 1);
            return BoreOrOutside(gauge, new Vector2(local.Y, local.Z).Length(), t.RadiiMm[index], t.InnerMm[index]);
        }
        return gauge == HandToolKind.Caliper ? "Kumpas: " + Blank.Describe() : null;
    }

    /// <summary>A point nearer the bore than the outside reads the bore.</summary>
    public static string BoreOrOutside(HandToolKind gauge, float pointRadiusMm, float outerMm, float innerMm) =>
        innerMm > 0f && Mathf.Abs(pointRadiusMm - innerMm) < Mathf.Abs(pointRadiusMm - outerMm)
            ? Gauges.Diameter(gauge, 2f * innerMm) is string bore ? "İç çap: " + bore : null
            : Gauges.Diameter(gauge, 2f * outerMm);

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

    /// <summary>A part off the lathe (radii from its -X end). Null when it
    /// is too short to be a part.</summary>
    public static StockItem CreateTurned(float[] radiiMm, float[] innerMm, float stepMm, MaterialKind material)
    {
        float length = (radiiMm.Length - 1) * stepMm;
        if (WorkpiecePartBuilder.Build(radiiMm, stepMm, -length / 2f, MaterialPreset.Get(material).DensityKgPerM3, innerMm: innerMm)
            is not WorkpiecePartBuilder.BuiltPart built)
            return null;
        float diameter = 2f * Mathf.Max(radiiMm.Max(), 0.1f);
        var item = new StockItem
        {
            Blank = new StockBlank(StockShape.CylinderAlongX, new Vector3(length, diameter, diameter), material),
            Turned = (radiiMm, innerMm, stepMm),
            Name = "TurnedPart",
            DisplayName = "Tornalanmış parça",
            Mass = Mathf.Max(0.01f, built.MassKg),
        };
        // The builder centres the part on its centre of mass; the stock frame is centred on its length.
        var offset = new Vector3(built.CentroidZmm * WorldScale.MmToM, 0f, 0f);
        item.AddChild(new MeshInstance3D { Mesh = built.Mesh, MaterialOverride = BuildMaterial(material), Position = offset });
        item.AddChild(new CollisionShape3D { Shape = built.Collision, Position = offset });
        return item;
    }

    /// <summary>Turns a turned part end for end in its own data, so its -X
    /// end is the other one (a chuck takes the -X end).</summary>
    public void ReverseTurned()
    {
        if (Turned is not { } t)
            return;
        var radii = t.RadiiMm.Reverse().ToArray();
        var inner = t.InnerMm.Reverse().ToArray();
        Turned = (radii, inner, t.StepMm);
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
