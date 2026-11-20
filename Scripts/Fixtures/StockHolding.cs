using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Machining;
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
    /// <summary>How it sits and what it holds, for a save.</summary>
    Godot.Collections.Dictionary SaveFixture();
    /// <summary>How far the part creeps under the cutting force per mm^3
    /// cut: 0 when it is clamped fully tight, a little when it is only snug.</summary>
    float SlipMmPerMm3 => 0f;
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
    /// <summary>What the cutting left on the surface, handed to the part.</summary>
    public SurfaceFinish Surface;

    public bool Occupied => Stock != null;

    /// <summary>How the stock is turned in its holder (quarter turns).</summary>
    public Basis Turn { get; private set; } = Basis.Identity;

    /// <summary>The stock's size along the holder's X, Y, Z (metres).</summary>
    public Vector3 ExtentsM => Stock == null ? Vector3.Zero : SquareTurn.Extents(Turn, Stock.Blank.BoundsM);

    /// <summary>Fixes the stock to its holder at this place (holder's frame).
    /// Stock that was machined before keeps its cut shape.</summary>
    public void Put(StockItem item, Node3D holder, Transform3D at)
    {
        Stock = item;
        Turn = SquareTurn.Snap(at.Basis);
        MachinedPart = item.GetChildren().OfType<VoxelStockVisual>().FirstOrDefault();
        WasCut = MachinedPart != null;
        item.Fixed = true;
        item.Freeze = true;
        item.Reparent(holder);
        item.Transform = at;
        if (item.Burred && holder is not BenchVise)
            Player.PlayerController.Notify(holder, "Çapaklı parça bağlandı: çapak oturmayı bozar, önce eğeyle al");
    }

    /// <summary>The held stock for a save; null when empty.</summary>
    public Godot.Collections.Dictionary Save() => Stock == null ? null : new Godot.Collections.Dictionary
    {
        ["item"] = CncTrainer.Save.SavedItems.Record(Stock), ["turn"] = Turn, ["cut"] = WasCut, ["ra"] = Surface.RaUm, ["chatter"] = Surface.Chatter, ["spring"] = Surface.SpringMm,
    };

    /// <summary>The saved stock made again under the holder it goes into; null when there was none.</summary>
    public static StockItem SavedItem(Godot.Collections.Dictionary data, Node holder)
    {
        if (data == null || !data.ContainsKey("item") || CncTrainer.Save.SavedItems.Make(data["item"].AsGodotDictionary()) is not StockItem item)
            return null;
        holder.AddChild(item);
        return item;
    }

    /// <summary>After the saved stock is back in: what had happened to it here.</summary>
    public void Restored(Godot.Collections.Dictionary data)
    {
        WasCut = data["cut"].AsBool();
        Surface = new SurfaceFinish(data["chatter"].AsBool(), data["ra"].AsSingle(), data["spring"].AsSingle());
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

    /// <summary>Empties the seat: the stock, untouched or with its machined
    /// shape, as a loose object that can be put in a fixture again.</summary>
    public RigidBody3D Take()
    {
        var stock = Stock;
        if (!WasCut && MachinedPart != null)
        {
            MachinedPart.QueueFree();
            ShowRaw(true);
        }
        else if (WasCut)
        {
            stock.DisplayName = "İşlenmiş parça";
            stock.FinishSurface(Surface);
        }
        stock.Fixed = false;
        stock.Reparent(stock.GetTree().CurrentScene);
        Stock = null;
        MachinedPart = null;
        return stock;
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
