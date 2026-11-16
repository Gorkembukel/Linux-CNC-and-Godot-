using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>Anything on a mill that holds the stock being machined: vise,
/// step clamps, centring plate, indexer chuck.</summary>
public interface IStockFixture
{
    StockSeat Seat { get; }
    bool HoldsFirmly { get; }
    /// <summary>Alarm text when a cut throws the part out.</summary>
    string LooseReason { get; }
    /// <summary>Checklist lines for the setup sheet.</summary>
    string SheetLines { get; }
    /// <summary>Why a tool of this radius with its tip here (the fixture's
    /// local frame, metres) is inside the fixture's own hardware, or null.</summary>
    string ToolCrash(Vector3 tip, float radius);
    /// <summary>Takes whatever is held out as a loose object.</summary>
    RigidBody3D TakeStock();
}

/// <summary>The stock a fixture holds: the raw piece the player brought,
/// and what it looks like once machining has started.</summary>
public sealed class StockSeat
{
    public StockItem Stock { get; private set; }
    /// <summary>Once machining starts the raw stock's look is replaced by this.</summary>
    public Node3D MachinedPart { get; private set; }
    /// <summary>The stock has had material removed.</summary>
    public bool WasCut;

    public bool Occupied => Stock != null;

    /// <summary>Fixes the stock to its holder at this place (holder's frame).</summary>
    public void Put(StockItem item, Node3D holder, Transform3D at)
    {
        Stock = item;
        MachinedPart = null;
        WasCut = false;
        item.Fixed = true;
        item.Freeze = true;
        item.Reparent(holder);
        item.Transform = at;
    }

    public void ShowRaw(bool visible)
    {
        if (Stock != null)
            foreach (var mesh in Stock.GetChildren().OfType<MeshInstance3D>())
                mesh.Visible = visible;
    }

    public void ShowMachined(Node3D machined)
    {
        MachinedPart = machined;
        ShowRaw(false);
    }

    /// <summary>Empties the seat, throwing away what was in it (something
    /// else has taken its place as the part).</summary>
    public void Discard()
    {
        Stock?.QueueFree();
        Stock = null;
        MachinedPart = null;
    }

    /// <summary>Empties the seat: the untouched stock again, or the machined
    /// part as a new carryable object where the stock was.</summary>
    public RigidBody3D Take()
    {
        var stock = Stock;
        var machined = MachinedPart;
        var scene = stock.GetTree().CurrentScene;
        RigidBody3D result = stock;
        if (!WasCut || machined == null)
        {
            machined?.QueueFree();
            ShowRaw(true);
            stock.Fixed = false;
            stock.Reparent(scene);
        }
        else
        {
            var part = new Carryable { Name = "Part", DisplayName = "İşlenmiş parça", Mass = stock.Mass };
            scene.AddChild(part);
            part.GlobalTransform = stock.GlobalTransform;
            part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = stock.Blank.BoundsM } });
            machined.Reparent(part);
            stock.QueueFree();
            result = part;
        }
        Stock = null;
        MachinedPart = null;
        return result;
    }
}

/// <summary>
/// How square something set down by hand lies to the machine axis, read with
/// the dial indicator and corrected with mallet taps on one end.
/// </summary>
public sealed class Tram
{
    public const float AlignedWithinMm = 0.03f;

    private readonly float _lengthMm;

    public float YawDeg { get; private set; }
    /// <summary>Indicator reading over the whole edge (mm).</summary>
    public float RunoutMm => Runout(YawDeg, _lengthMm);
    public bool Aligned => Mathf.Abs(RunoutMm) <= AlignedWithinMm;

    /// <summary>yawDeg null = the random small angle of something put down
    /// by hand, up to maxDeg, in hundredths of a degree and never zero.</summary>
    public Tram(float lengthMm, float? yawDeg, float maxDeg)
    {
        _lengthMm = lengthMm;
        if (yawDeg is float given)
            YawDeg = given;
        else
        {
            float yaw = Mathf.Round((float)GD.RandRange(-maxDeg, maxDeg) * 100f) / 100f;
            YawDeg = Mathf.Abs(yaw) < 0.05f ? 0.05f : yaw;
        }
    }

    public static float Runout(float yawDeg, float lengthMm) => Mathf.Tan(Mathf.DegToRad(yawDeg)) * lengthMm;

    /// <summary>One tap on the end at this side: a tenth of a degree, or a
    /// hundredth when `fine`.</summary>
    public static float Tapped(float yawDeg, float lookLocalX, bool fine) =>
        Mathf.Round((yawDeg + (lookLocalX >= 0f ? 1f : -1f) * (fine ? 0.01f : 0.1f)) * 100f) / 100f;

    public void Tap(float lookLocalX, bool fine) => YawDeg = Tapped(YawDeg, lookLocalX, fine);

    private string Reading => System.FormattableString.Invariant($"{RunoutMm:+0.00;-0.00} mm");

    public string DialText => Aligned
        ? $"Komparatör: sapma {Reading}  ->  hizalı"
        : $"Komparatör: kenar boyunca sapma {Reading} (plastik çekiçle uçtan vur)";

    public string TapPrompt(string what, bool fine, string fineHint) =>
        $"[Tık] {what} bu ucuna vur   sapma {Reading}" + (fine ? "  (ince ayar)" : $"  (kaba ayar; ince ayar için {fineHint})");

    public string SheetLine(string label) => Sheet.Mark(Aligned) + $"{label} (komparatör)  sapma {Reading}\n";
}

/// <summary>Formatting shared by every setup sheet.</summary>
public static class Sheet
{
    public static string Mark(bool done) => done ? "[x] " : "[ ] ";
}
