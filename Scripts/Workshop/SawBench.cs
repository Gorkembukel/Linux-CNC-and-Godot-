using Godot;
using CncTrainer.Chips;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Workshop;

/// <summary>A workbench with a bench vise at its left end, for sawing stock
/// to length by hand. The node's origin is on the floor; the player works
/// from its +Z side.</summary>
[Tool]
public partial class SawBench : Node3D
{
    private const float TopY = 0.85f;

    public BenchVise Vise { get; private set; }

    public override void _Ready()
    {
        BodyParts.Clear(this);
        var wood = BodyParts.Mat(new Color(0.5f, 0.36f, 0.2f), 0f, 0.85f);
        BodyParts.SolidBox(this, "Top", new Vector3(1.2f, 0.05f, 0.6f), new Vector3(0f, TopY - 0.025f, 0f), wood);
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -0.24f, 0.24f })
                BodyParts.SolidBox(this, "Leg", new Vector3(0.06f, TopY - 0.05f, 0.06f), new Vector3(x, (TopY - 0.05f) / 2f, z), BodyParts.DarkSteel);
        AddChild(new Label3D
        {
            Text = "KESİM",
            PixelSize = 0.002f,
            FontSize = 40,
            Position = new Vector3(0f, TopY - 0.1f, 0.301f),
            Modulate = new Color(0.9f, 0.9f, 0.9f),
        });
        Vise = new BenchVise { Name = "BenchVise", Position = new Vector3(-0.4f, TopY, 0.1f) };
        AddChild(Vise);
    }
}

/// <summary>
/// A bench vise: stock goes in lengthwise along its X with the -X end at the
/// jaws' left face, the rest sticking out to the right over the bench, so a
/// saw cut drops the right-hand piece. Click with stock in hand to clamp it,
/// with an empty hand to take it out.
/// </summary>
public partial class BenchVise : StaticBody3D, IInteractable, ISawable
{
    private const float M = WorldScale.MmToM;
    private const float JawWidthM = 0.1f, JawHeightM = 0.06f, FloorM = 0.03f;
    /// <summary>Material the blade takes out (mm).</summary>
    public const float KerfMm = 1f;

    public StockSeat Seat { get; } = new();

    public override void _Ready()
    {
        var iron = BodyParts.Mat(new Color(0.18f, 0.32f, 0.5f), 0.4f, 0.5f);
        BodyParts.Box(this, "Base", new Vector3(0.16f, FloorM, 0.18f), new Vector3(0f, FloorM / 2f, 0f), iron);
        BodyParts.Box(this, "FixedJaw", new Vector3(JawWidthM, JawHeightM, 0.03f), new Vector3(0f, FloorM + JawHeightM / 2f, -0.06f), iron);
        BodyParts.Box(this, "MovingJaw", new Vector3(JawWidthM, JawHeightM, 0.03f), new Vector3(0f, FloorM + JawHeightM / 2f, 0.06f), iron);
        BodyParts.Cylinder(this, "Screw", 0.008f, 0.12f, new Vector3(0f, FloorM + 0.02f, 0.13f), Vector3.Back, BodyParts.Steel);
        BodyParts.Cylinder(this, "Handle", 0.006f, 0.16f, new Vector3(0f, FloorM + 0.02f, 0.19f), Vector3.Right, BodyParts.Steel);
        CollisionLayer = Fastener.InteractionLayer;
        CollisionMask = 0;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.16f, FloorM + JawHeightM, 0.18f) }, Position = new Vector3(0f, (FloorM + JawHeightM) / 2f, 0f) });
    }

    /// <summary>Where a piece of this size sits: on the vise floor, its -X end at the jaws' left face.</summary>
    private Transform3D SeatFor(StockItem item)
    {
        var size = item.Blank.BoundsM;
        return new Transform3D(Basis.Identity, new Vector3(-JawWidthM / 2f + size.X / 2f, FloorM + size.Y / 2f, 0f));
    }

    public string Prompt(PlayerController player) => (player.Held, Seat.Stock) switch
    {
        (StockItem item, null) => $"[Sol tık] Mengeneye bağla: {item.DisplayName}",
        (null, StockItem held) => $"[Sol tık] Mengeneden al: {held.DisplayName}",
        (null, null) => "Mengene boş",
        _ => null,
    };

    public void Use(PlayerController player)
    {
        if (player.Held is StockItem item && Seat.Stock == null)
        {
            player.ReleaseHeld();
            Seat.Put(item, this, SeatFor(item));
        }
        else if (player.Held == null && Seat.Stock != null)
            player.Give(Seat.Take());
    }

    public MaterialKind SawMaterial => Seat.Stock?.Blank.Material ?? MaterialKind.Steel;

    public SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        var stock = Seat.Stock;
        why = null;
        if (stock == null)
        {
            why = "Mengenede parça yok";
            return null;
        }
        float x = stock.ToLocal(globalPoint).X / M, half = stock.Blank.LengthMm / 2f;
        float jawEnd = -half + JawWidthM / M;
        SawCut? plan;
        if (angleDeg == 0f && stock.Machined == null && stock.SectionAt(x) is { } section)
        {
            var basis = stock.GlobalBasis.Orthonormalized();
            plan = new SawCut(stock.ToGlobal(new Vector3(x * M, 0f, 0f)), basis.X, -basis.Y, section.WidthMm * M, section.HeightMm * M, section.AreaMm2, x);
        }
        else
            plan = VoxelSaw.Plan(stock.Voxels(), stock.GlobalTransform * stock.VoxelFrame, globalPoint,
                VoxelSaw.Normal(stock.GlobalBasis.Orthonormalized().X, angleDeg), angleDeg, x);
        if (plan is not { } cut)
            return null;
        // How far along the stock an angled cut reaches either side of where it is aimed.
        float reach = cut.WidthM / M / 2f * Mathf.Abs(Mathf.Sin(Mathf.DegToRad(angleDeg)));
        if (x - reach < jawEnd + 3f)
        {
            why = "Çenelerin içi kesilmez: mengenenin dışından kes";
            return null;
        }
        if (x + reach > half - 2f)
        {
            why = "Uca çok yakın";
            return null;
        }
        return cut;
    }

    public void FinishCut(SawCut cut)
    {
        var stock = Seat.Stock;
        if (stock == null)
            return;
        var scene = GetTree().CurrentScene;
        if (cut.AngleDeg == 0f && stock.Machined == null)
        {
            var whole = stock.GlobalTransform;
            var (near, _, far, farAt) = stock.SplitAt(cut.AtMm, KerfMm);
            Seat.Discard();
            if (near != null)
            {
                scene.AddChild(near);
                Seat.Put(near, this, SeatFor(near));
            }
            if (far != null)
            {
                scene.AddChild(far);
                far.GlobalTransform = whole * new Transform3D(Basis.Identity, new Vector3(farAt * M, 0f, 0f));
            }
            return;
        }

        var voxels = stock.Voxels().Copy();
        var voxelGlobal = stock.GlobalTransform * stock.VoxelFrame;
        var inJaws = stock.ToGlobal(new Vector3((-stock.Blank.LengthMm / 2f) * M + JawWidthM / 2f, 0f, 0f));
        var (dropped, _) = VoxelSaw.Split(voxels, voxelGlobal, cut, inJaws);
        var material = stock.Blank.Material;
        Seat.Discard();
        if (VoxelSaw.Spawn(voxels, voxelGlobal, material, scene) is StockItem kept)
            Seat.Put(kept, this, GlobalTransform.AffineInverse() * kept.GlobalTransform);
        VoxelSaw.Spawn(dropped, voxelGlobal, material, scene);
    }
}
