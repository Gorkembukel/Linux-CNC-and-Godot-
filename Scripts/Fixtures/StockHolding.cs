using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>Anything on a mill that holds the stock being machined: vise,
/// step clamps, centring plate, indexer chuck.</summary>
public interface IStockFixture
{
    StockItem Stock { get; }
    /// <summary>Once machining starts the raw stock is replaced by this.</summary>
    Node3D MachinedPart { get; }
    /// <summary>The stock in the fixture has had material removed.</summary>
    bool WasCut { get; set; }
    bool HoldsFirmly { get; }
    /// <summary>Alarm text when a cut throws the part out.</summary>
    string LooseReason { get; }
    /// <summary>Checklist lines for the setup sheet.</summary>
    string SheetLines { get; }
    void ShowMachined(Node3D machined);
    /// <summary>Takes whatever is held out as a loose object.</summary>
    RigidBody3D TakeStock();
}

public static class StockHolding
{
    public const float AlignedWithinMm = 0.03f;

    public static string Mark(bool done) => done ? "[x] " : "[ ] ";

    public static void ShowRaw(StockItem stock, bool visible)
    {
        foreach (var mesh in stock.GetChildren().OfType<MeshInstance3D>())
            mesh.Visible = visible;
    }

    /// <summary>The untouched stock again, or the machined part as a new
    /// carryable object where the stock was.</summary>
    public static RigidBody3D Release(StockItem stock, Node3D machined, bool wasCut)
    {
        var scene = stock.GetTree().CurrentScene;
        if (!wasCut || machined == null)
        {
            machined?.QueueFree();
            ShowRaw(stock, true);
            stock.Fixed = false;
            stock.Reparent(scene);
            return stock;
        }

        var part = new Carryable { Name = "Part", DisplayName = "İşlenmiş parça", Mass = stock.Mass };
        scene.AddChild(part);
        part.GlobalTransform = stock.GlobalTransform;
        part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = stock.Blank.BoundsM } });
        machined.Reparent(part);
        stock.QueueFree();
        return part;
    }

    /// <summary>Dial indicator reading: how far an edge of this length runs
    /// out of parallel with the machine axis (mm).</summary>
    public static float RunoutMm(float yawDeg, float lengthMm) => Mathf.Tan(Mathf.DegToRad(yawDeg)) * lengthMm;

    public static string DialText(float runoutMm) => Mathf.Abs(runoutMm) <= AlignedWithinMm
        ? System.FormattableString.Invariant($"Komparatör: sapma {runoutMm:+0.00;-0.00} mm  ->  hizalı")
        : System.FormattableString.Invariant($"Komparatör: kenar boyunca sapma {runoutMm:+0.00;-0.00} mm (plastik çekiçle uçtan vur)");

    /// <summary>A random out-of-square angle for something set down by
    /// hand, in hundredths of a degree and never zero.</summary>
    public static float HandPlacedYawDeg(float maxDeg)
    {
        float yaw = Mathf.Round((float)GD.RandRange(-maxDeg, maxDeg) * 100f) / 100f;
        return Mathf.Abs(yaw) < 0.05f ? 0.05f : yaw;
    }

    /// <summary>One mallet tap on the end the player looks at: a tenth of
    /// a degree, or a hundredth when `fine`.</summary>
    public static float Tap(float yawDeg, float lookLocalX, bool fine) =>
        Mathf.Round((yawDeg + (lookLocalX >= 0f ? 1f : -1f) * (fine ? 0.01f : 0.1f)) * 100f) / 100f;
}
