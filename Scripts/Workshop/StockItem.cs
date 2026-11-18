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
    /// -X end to the +X end, and the half sizes across the axis of the block
    /// it was turned from ((0, 0) = round bar). Null for raw or milled stock.</summary>
    public (float[] RadiiMm, float[] InnerMm, float StepMm, Vector2 SquareHalfMm)? Turned { get; private set; }

    /// <summary>Size a chuck's jaws close on when the -X end goes in first.</summary>
    public float GripDiameterMm => Turned is { } t ? 2f * LatheSection.Reach(t.RadiiMm[0], t.SquareHalfMm) : Blank.DiameterMm;

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
            return SectionReading(gauge, new Vector2(local.Y, local.Z).Length(), t.RadiiMm[index], t.InnerMm[index], t.SquareHalfMm);
        }
        return gauge == HandToolKind.Caliper ? "Kumpas: " + Blank.Describe() : null;
    }

    /// <summary>Like BoreOrOutside, for a section that may still have the
    /// block's flats: then the caliper reads across them.</summary>
    public static string SectionReading(HandToolKind gauge, float pointRadiusMm, float outerMm, float innerMm, Vector2 squareHalfMm)
    {
        if (squareHalfMm == Vector2.Zero || outerMm <= Mathf.Min(squareHalfMm.X, squareHalfMm.Y)
            || (innerMm > 0f && Mathf.Abs(pointRadiusMm - innerMm) < Mathf.Abs(pointRadiusMm - outerMm)))
            return BoreOrOutside(gauge, pointRadiusMm, outerMm, innerMm);
        var across = 2f * new Vector2(Mathf.Min(outerMm, squareHalfMm.X), Mathf.Min(outerMm, squareHalfMm.Y));
        return Gauges.Diameter(gauge, across.X) is string reading
            ? System.FormattableString.Invariant($"{reading} (köşeli kesit {across.X:0.00} x {across.Y:0.00} mm, köşelerde Ø{2f * Mathf.Min(outerMm, LatheSection.Corner(squareHalfMm)):0.00})")
            : null;
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
    public static StockItem CreateTurned(float[] radiiMm, float[] innerMm, float stepMm, MaterialKind material, Vector2 squareHalfMm = default)
    {
        float length = (radiiMm.Length - 1) * stepMm;
        if (WorkpiecePartBuilder.Build(radiiMm, stepMm, -length / 2f, MaterialPreset.Get(material).DensityKgPerM3, innerMm: innerMm, squareHalfMm: squareHalfMm)
            is not WorkpiecePartBuilder.BuiltPart built)
            return null;
        float radius = Mathf.Max(radiiMm.Max(), 0.1f);
        bool flats = squareHalfMm != Vector2.Zero && radius > Mathf.Min(squareHalfMm.X, squareHalfMm.Y);
        // Block sizes: X length, Y across the jaws (stock Z), Z height (stock Y).
        var blank = flats
            ? new StockBlank(StockShape.Block, new Vector3(length, 2f * Mathf.Min(radius, squareHalfMm.Y), 2f * Mathf.Min(radius, squareHalfMm.X)), material)
            : new StockBlank(StockShape.CylinderAlongX, new Vector3(length, 2f * radius, 2f * radius), material);
        var item = new StockItem
        {
            Blank = blank,
            Turned = (radiiMm, innerMm, stepMm, flats ? squareHalfMm : Vector2.Zero),
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

    /// <summary>Its section across the length at local x (mm from the
    /// centre): width (local Z), height (local Y) and solid area. Null for
    /// milled stock, whose shape is a voxel field.</summary>
    public (float WidthMm, float HeightMm, float AreaMm2)? SectionAt(float xMm)
    {
        if (GetChildren().OfType<VoxelStockVisual>().Any())
            return null;
        if (Turned is { } t)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((xMm + Blank.LengthMm / 2f) / t.StepMm), 0, t.RadiiMm.Length - 1);
            float across = 2f * LatheSection.Reach(t.RadiiMm[i], t.SquareHalfMm);
            return (across, across, LatheSection.Area(t.RadiiMm[i], t.SquareHalfMm) - Mathf.Pi * t.InnerMm[i] * t.InnerMm[i]);
        }
        var size = Blank.BoundsM / WorldScale.MmToM;
        return (size.Z, size.Y, Blank.Shape == StockShape.Block ? size.Y * size.Z : Mathf.Pi * size.Y * size.Y / 4f);
    }

    /// <summary>The two pieces a straight cut at local x (mm from the
    /// centre) leaves with kerfMm of material gone between them: the -X one
    /// (Near) and the +X one (Far), each centred on its own length, at
    /// NearAtMm / FarAtMm along this one's X. A piece too short to be one
    /// is null (it goes with the chips). Milled stock is not split here.</summary>
    public (StockItem Near, float NearAtMm, StockItem Far, float FarAtMm) SplitAt(float xMm, float kerfMm)
    {
        float length = Blank.LengthMm;
        if (Turned is { } t)
        {
            int last = t.RadiiMm.Length - 1;
            int i0 = Mathf.Clamp(Mathf.RoundToInt((xMm - kerfMm / 2f + length / 2f) / t.StepMm), 0, last);
            int i1 = Mathf.Clamp(Mathf.RoundToInt((xMm + kerfMm / 2f + length / 2f) / t.StepMm), 0, last);
            var near = i0 >= 1 ? CreateTurned(t.RadiiMm[..(i0 + 1)], t.InnerMm[..(i0 + 1)], t.StepMm, Blank.Material, t.SquareHalfMm) : null;
            var far = last - i1 >= 1 ? CreateTurned(t.RadiiMm[i1..], t.InnerMm[i1..], t.StepMm, Blank.Material, t.SquareHalfMm) : null;
            return (near, -length / 2f + i0 * t.StepMm / 2f, far, -length / 2f + (i1 + last) * t.StepMm / 2f);
        }
        float nearLength = xMm + length / 2f - kerfMm / 2f, farLength = length / 2f - xMm - kerfMm / 2f;
        StockItem Piece(float l) => l < 1f ? null : Create(Blank with { SizeMm = Blank.SizeMm with { X = l } });
        return (Piece(nearLength), -length / 2f + nearLength / 2f, Piece(farLength), length / 2f - farLength / 2f);
    }

    /// <summary>Turns a turned part end for end in its own data, so its -X
    /// end is the other one (a chuck takes the -X end).</summary>
    public void ReverseTurned()
    {
        if (Turned is not { } t)
            return;
        Turned = (t.RadiiMm.Reverse().ToArray(), t.InnerMm.Reverse().ToArray(), t.StepMm, t.SquareHalfMm);
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
