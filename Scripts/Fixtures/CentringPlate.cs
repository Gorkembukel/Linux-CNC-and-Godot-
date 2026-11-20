using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// Fixture plate for a rotary table: its pin drops into the table's centre
/// bore, so it always sits exactly on the rotary axis, and its self-centring
/// jaws put the stock's centre there too. Without RTCP this is what makes
/// the work zero known: X/Y zero is the rotary centre. Local frame: origin
/// at the centre of the plate's underside.
/// </summary>
public partial class CentringPlate : BoltedFixture, IInteractable, IStockFixture
{
    public const float Thickness = 0.02f;
    private const float Size = 0.2f;
    private const float JawHeight = 0.025f;
    private const float MaxStockMm = 150f;

    public StockSeat Seat { get; } = new();
    public int Torque { get; private set; }

    public bool HoldsFirmly => BoltsTight && Seat.Occupied && Torque >= 2;
    public string LooseReason => BoltsTight
        ? "PARÇA FIRLADI: merkezleme plakasının çeneleri sıkılmamıştı"
        : "PARÇA FIRLADI: merkezleme plakasının cıvataları tam sıkılmamıştı";
    protected override bool Loaded => Seat.Occupied;

    private readonly MeshInstance3D[] _jaws = new MeshInstance3D[2];

    public static CentringPlate Create()
    {
        var plate = new CentringPlate { Name = "CentringPlate", DisplayName = "Merkezleme plakası", Mass = 9f };
        BodyParts.Box(plate, "Plate", new Vector3(Size, Thickness, Size), new Vector3(0f, Thickness / 2f, 0f), BodyParts.Steel);
        BodyParts.Cylinder(plate, "Pin", 0.012f, 0.02f, new Vector3(0f, -0.01f, 0f), Vector3.Up, BodyParts.DarkSteel);
        for (int i = 0; i < 2; i++)
            plate._jaws[i] = BodyParts.Box(plate, "Jaw", new Vector3(0.12f, JawHeight, 0.02f), Vector3.Zero, BodyParts.DarkSteel);
        plate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Size, Thickness, Size) }, Position = new Vector3(0f, Thickness / 2f, 0f) });
        plate.Refresh();
        return plate;
    }

    private void Refresh()
    {
        float half = Seat.Occupied ? Seat.ExtentsM.Z / 2f : 0.06f;
        for (int i = 0; i < 2; i++)
            _jaws[i].Position = new Vector3(0f, Thickness + JawHeight / 2f, (i == 0 ? -1f : 1f) * (half + 0.01f));
    }

    /// <summary>Drops the plate's pin into the table centre and opens its
    /// four bolt holes.</summary>
    public void MountOn(MillTable table)
    {
        Mount(table, new[] { -0.08f, 0.08f }.SelectMany(x => new[] { -0.08f, 0.08f }.Select(z => new Vector3(x, Thickness, z))));
        Transform = Transform3D.Identity;
    }

    private bool Fits(StockItem item, Basis turn)
    {
        var mm = SquareTurn.Extents(turn, item.Blank.BoundsM) / WorldScale.MmToM;
        return item.Blank.Shape == StockShape.Block && mm.X <= MaxStockMm && mm.Z <= MaxStockMm;
    }

    public void PlaceStock(StockItem item, Basis? turn = null)
    {
        Torque = 0;
        var t = turn ?? Basis.Identity;
        Seat.Put(item, this, new Transform3D(t, new Vector3(0f, Thickness + SquareTurn.Extents(t, item.Blank.BoundsM).Y / 2f, 0f)));
        Refresh();
    }

    /// <summary>One wheel notch on the jaw screw: +1 tightens.</summary>
    public void TurnScrew(int steps)
    {
        if (Seat.Occupied)
            Torque = Mathf.Clamp(Torque + steps, 0, 3);
    }

    public string SheetLines =>
        Sheet.Mark(true) + "Merkezleme plakası tablada (pim merkezde)\n"
        + BoltsSheetLine("Plaka cıvataları tam sıkı")
        + Sheet.Mark(Seat.Occupied) + "Kütük plakada (kendiliğinden merkezde)\n"
        + Sheet.Mark(Torque >= 3) + $"Çeneler sıkıldı  {Torque}/3\n"
        + "    X/Y sıfırı = döner tabla merkezi\n";

    public string ToolCrash(Vector3 tip, float radius) =>
        tip.Y < Thickness + JawHeight && Mathf.Abs(tip.X) < Size / 2f + radius && Mathf.Abs(tip.Z) < Size / 2f + radius
            ? "ÇARPMA: takım merkezleme plakasına girdi"
            : null;

    public RigidBody3D TakeStock()
    {
        Torque = 0;
        var body = Seat.Take();
        Refresh();
        return body;
    }

    public Godot.Collections.Dictionary SaveFixture() => new()
    {
        ["kind"] = nameof(CentringPlate), ["bolts"] = SaveBolts(), ["torque"] = Torque, ["seat"] = Seat.Save() is { } seat ? seat : default,
    };

    public static void RestoreOn(MillTable table, Godot.Collections.Dictionary d)
    {
        var plate = Create();
        table.AddChild(plate);
        plate.MountOn(table);
        plate.RestoreBolts(d["bolts"].AsInt32Array());
        var seat = CncTrainer.Save.SaveCodec.Dict(d["seat"]);
        if (StockSeat.SavedItem(seat, plate) is StockItem item)
        {
            plate.PlaceStock(item, seat["turn"].AsBasis());
            plate.Seat.Restored(seat);
        }
        plate.Torque = d["torque"].AsInt32();
    }

    public string Prompt(PlayerController player)
    {
        if (!IsMounted)
            return null;
        if (player.HeldTool?.Kind == HandToolKind.ViseHandle)
            return Seat.Occupied ? $"[Tekerlek] Çeneleri sık / gevşet   sıkma {Torque}/3" : "Plakada kütük yok";
        if (player.Held is StockItem item && !Seat.Occupied)
            return Fits(item, player.HeldTurnIn(this)) ? "[Tık] Kütüğü plakaya koy (çeneler merkezler)" : $"Plaka en fazla {MaxStockMm:0} x {MaxStockMm:0} mm blok tutar";
        if (player.Held == null && Seat.Occupied)
            return Torque == 0 ? "[Tık] Parçayı al" : $"Çeneler sıkılı ({Torque}/3). Parçayı almak için mengene koluyla gevşet";
        return Seat.Occupied ? null : "Merkezleme plakası: blok kütük getir";
    }

    public void Use(PlayerController player)
    {
        if (!IsMounted)
            return;
        if (player.Held is StockItem item && !Seat.Occupied && Fits(item, player.HeldTurnIn(this)))
        {
            var turn = player.HeldTurnIn(this);
            player.ReleaseHeld();
            PlaceStock(item, turn);
        }
        else if (player.Held == null && Seat.Occupied && Torque == 0)
            player.Give(TakeStock());
    }

    public void Turn(PlayerController player, int steps)
    {
        if (IsMounted && player.HeldTool?.Kind == HandToolKind.ViseHandle)
            TurnScrew(steps);
    }
}
