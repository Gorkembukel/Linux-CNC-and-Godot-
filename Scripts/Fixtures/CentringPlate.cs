using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// Fixture plate for a rotary table: its pins drop into the table's centre
/// bore, so it always sits exactly on the rotary axis, and its self-centring
/// jaws put the stock's centre there too. Without RTCP this is what makes
/// the work zero known: X/Y zero is the rotary centre. Local frame: origin
/// at the centre of the plate's underside.
/// </summary>
public partial class CentringPlate : Carryable, IInteractable, IStockFixture
{
    public const float Thickness = 0.02f;
    private const float Size = 0.2f;
    private const float JawHeight = 0.025f;
    private const float MaxStockMm = 150f;

    public MillTable Table { get; private set; }
    public readonly List<Fastener> Bolts = new();
    public int Torque { get; private set; }
    public StockItem Stock { get; private set; }
    public Node3D MachinedPart { get; private set; }
    public bool WasCut { get; set; }

    public bool IsMounted => Table != null;
    public bool BoltsTight => Bolts.Count > 0 && Bolts.All(b => b.IsTight);
    public bool HoldsFirmly => BoltsTight && Stock != null && Torque >= 2;
    public string LooseReason => BoltsTight
        ? "PARÇA FIRLADI: merkezleme plakasının çeneleri sıkılmamıştı"
        : "PARÇA FIRLADI: merkezleme plakasının cıvataları tam sıkılmamıştı";
    public override bool CanPickUp => !IsMounted || (Stock == null && Bolts.All(b => !b.Present));

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
        float half = Stock != null ? Stock.Blank.BoundsM.Z / 2f : 0.06f;
        for (int i = 0; i < 2; i++)
            _jaws[i].Position = new Vector3(0f, Thickness + JawHeight / 2f, (i == 0 ? -1f : 1f) * (half + 0.01f));
    }

    public override void OnPickedUp()
    {
        if (!IsMounted)
            return;
        foreach (var bolt in Bolts)
            bolt.QueueFree();
        Bolts.Clear();
        Table = null;
        Reparent(GetTree().CurrentScene);
    }

    /// <summary>Drops the plate's pin into the table centre and opens its
    /// four bolt holes.</summary>
    public void MountOn(MillTable table)
    {
        Table = table;
        Freeze = true;
        Reparent(table);
        Transform = Transform3D.Identity;
        foreach (float x in new[] { -0.08f, 0.08f })
            foreach (float z in new[] { -0.08f, 0.08f })
            {
                var bolt = new Fastener { Name = "HoldDown", Position = new Vector3(x, Thickness, z) };
                AddChild(bolt);
                Bolts.Add(bolt);
            }
    }

    private static bool Fits(StockItem item) =>
        item.Blank.Shape == StockShape.Block && item.Blank.SizeMm.X <= MaxStockMm && item.Blank.SizeMm.Y <= MaxStockMm;

    public void PlaceStock(StockItem item)
    {
        Stock = item;
        Torque = 0;
        WasCut = false;
        item.Fixed = true;
        item.Freeze = true;
        item.Reparent(this);
        item.Transform = new Transform3D(Basis.Identity, new Vector3(0f, Thickness + item.Blank.BoundsM.Y / 2f, 0f));
        Refresh();
    }

    /// <summary>One wheel notch on the jaw screw: +1 tightens.</summary>
    public void TurnScrew(int steps)
    {
        if (Stock != null)
            Torque = Mathf.Clamp(Torque + steps, 0, 3);
    }

    public string SheetLines
    {
        get
        {
            int tight = Bolts.Count(b => b.IsTight);
            string text = StockHolding.Mark(true) + "Merkezleme plakası tablada (pim merkezde)\n";
            text += StockHolding.Mark(BoltsTight) + $"Plaka cıvataları tam sıkı  {tight}/{Bolts.Count}\n";
            text += StockHolding.Mark(Stock != null) + "Kütük plakada (kendiliğinden merkezde)\n";
            text += StockHolding.Mark(Torque >= 3) + $"Çeneler sıkıldı  {Torque}/3\n";
            text += "    X/Y sıfırı = döner tabla merkezi\n";
            return text;
        }
    }

    public void ShowMachined(Node3D machined)
    {
        MachinedPart = machined;
        StockHolding.ShowRaw(Stock, false);
    }

    public RigidBody3D TakeStock()
    {
        var stock = Stock;
        var machined = MachinedPart;
        Stock = null;
        MachinedPart = null;
        Torque = 0;
        Refresh();
        return StockHolding.Release(stock, machined, WasCut);
    }

    public string Prompt(PlayerController player)
    {
        if (!IsMounted)
            return null;
        if (player.HeldTool?.Kind == HandToolKind.ViseHandle)
            return Stock != null ? $"[Tekerlek] Çeneleri sık / gevşet   sıkma {Torque}/3" : "Plakada kütük yok";
        if (player.Held is StockItem item && Stock == null)
            return Fits(item) ? "[Tık] Kütüğü plakaya koy (çeneler merkezler)" : $"Plaka en fazla {MaxStockMm:0} x {MaxStockMm:0} mm blok tutar";
        if (player.Held == null && Stock != null)
            return Torque == 0 ? "[Tık] Parçayı al" : $"Çeneler sıkılı ({Torque}/3). Parçayı almak için mengene koluyla gevşet";
        return Stock == null ? "Merkezleme plakası: blok kütük getir" : null;
    }

    public void Use(PlayerController player)
    {
        if (!IsMounted)
            return;
        if (player.Held is StockItem item && Stock == null && Fits(item))
        {
            player.ReleaseHeld();
            PlaceStock(item);
        }
        else if (player.Held == null && Stock != null && Torque == 0)
            player.Give(TakeStock());
    }

    public void Turn(PlayerController player, int steps)
    {
        if (IsMounted && player.HeldTool?.Kind == HandToolKind.ViseHandle)
            TurnScrew(steps);
    }
}
