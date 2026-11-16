using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// Stock set straight on the mill table and held down with step clamps.
/// Nothing squares it: it lies at the small angle the player put it down
/// at until it is indicated and tapped straight. Each clamp's stud sits in a
/// T-slot beside one end of the stock. Local frame: origin on the table
/// under the stock's centre, X along the stock.
/// </summary>
public partial class TableClamping : Node3D, IInteractable, IStockFixture
{
    private const float M = WorldScale.MmToM;

    public StockItem Stock { get; private set; }
    public Node3D MachinedPart { get; private set; }
    public bool WasCut { get; set; }
    public float YawDeg { get; private set; }

    private MillTable _table;
    private Vector3 _at;
    private bool _onChips;
    private readonly List<(Node3D Node, Fastener Nut, int Side, float SlotZ)> _clamps = new();

    public float RunoutMm => StockHolding.RunoutMm(YawDeg, Stock.Blank.SizeMm.X);
    public bool Aligned => Mathf.Abs(RunoutMm) <= StockHolding.AlignedWithinMm;
    public int ClampCount => _clamps.Count;
    private bool AnyTight => _clamps.Any(c => c.Nut.IsTight);

    public bool HoldsFirmly => _clamps.Any(c => c.Side < 0 && c.Nut.IsTight) && _clamps.Any(c => c.Side > 0 && c.Nut.IsTight);
    public string LooseReason => _clamps.Count < 2
        ? "PARÇA FIRLADI: kütük pabuçla bağlanmamıştı (iki uçtan birer pabuç)"
        : "PARÇA FIRLADI: pabuç somunları tam sıkılmamıştı";

    /// <summary>Sets the stock down on the table at this spot (table frame).
    /// yawDeg null = the small random angle of something put down by hand.</summary>
    public static TableClamping Place(MillTable table, StockItem item, Vector3 tablePoint, float? yawDeg = null)
    {
        var fixture = new TableClamping
        {
            Name = "Clamping",
            Stock = item,
            _table = table,
            _at = new Vector3(tablePoint.X, 0f, tablePoint.Z),
            _onChips = table.Dirty,
            YawDeg = yawDeg ?? StockHolding.HandPlacedYawDeg(1.5f),
        };
        table.AddChild(fixture);
        fixture.ApplyPlacement();
        item.Fixed = true;
        item.Freeze = true;
        item.Reparent(fixture);
        item.Transform = new Transform3D(Basis.Identity, new Vector3(0f, item.Blank.BoundsM.Y / 2f, 0f));
        return fixture;
    }

    // Stock set down on chips does not lie flat.
    private void ApplyPlacement()
    {
        Transform = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(YawDeg)) * new Basis(Vector3.Right, _onChips ? Mathf.DegToRad(0.15f) : 0f), _at);
    }

    /// <summary>A mallet tap on one end; the stock only moves while no
    /// clamp is fully tight, and only a little once one is snug.</summary>
    public void Tap(float lookLocalX)
    {
        if (AnyTight)
            return;
        YawDeg = StockHolding.Tap(YawDeg, lookLocalX, _clamps.Any(c => c.Nut.IsSnug));
        ApplyPlacement();
    }

    /// <summary>The T-slot a clamp at this spot (local) would use, or null
    /// when no slot runs under that part of the stock.</summary>
    private float? SlotFor(Vector3 local)
    {
        float halfZ = Stock.Blank.BoundsM.Z / 2f;
        foreach (float slot in _table.SlotsNear(_at.Z + local.Z))
            if (Mathf.Abs(slot - _at.Z) <= halfZ - 0.005f)
                return slot;
        return null;
    }

    /// <summary>Fits a clamp on the end of the stock nearest the given
    /// point (local frame). False when there is no free T-slot there.</summary>
    public bool AddClamp(Vector3 local)
    {
        int side = local.X >= 0f ? 1 : -1;
        if (SlotFor(local) is not float slot || _clamps.Any(c => c.Side == side && c.SlotZ == slot))
            return false;

        var bounds = Stock.Blank.BoundsM;
        var node = new Node3D { Name = "StepClamp", Position = new Vector3(side * bounds.X / 2f, 0f, slot - _at.Z) };
        AddChild(node);
        BodyParts.Cylinder(node, "Stud", 0.006f, bounds.Y + 0.03f, new Vector3(side * 0.03f, (bounds.Y + 0.03f) / 2f, 0f), Vector3.Up, BodyParts.Steel);
        BodyParts.Box(node, "Strap", new Vector3(0.07f, 0.012f, 0.03f), new Vector3(side * 0.01f, bounds.Y + 0.006f, 0f), BodyParts.DarkSteel);
        BodyParts.Box(node, "StepBlock", new Vector3(0.03f, bounds.Y, 0.03f), new Vector3(side * 0.06f, bounds.Y / 2f, 0f), BodyParts.Steel);
        var nut = new Fastener { Name = "Nut", Position = new Vector3(side * 0.03f, bounds.Y + 0.012f, 0f), ItemFactory = ClampItem.Create };
        node.AddChild(nut);
        nut.Fit();
        nut.Removed = () =>
        {
            _clamps.RemoveAll(c => c.Node == node);
            node.QueueFree();
        };
        _clamps.Add((node, nut, side, slot));
        return true;
    }

    /// <summary>For tests and scripted setups.</summary>
    public void TightenAll()
    {
        foreach (var clamp in _clamps)
            clamp.Nut.SetTight();
    }

    /// <summary>Is a tool of this radius, with its tip here (local frame),
    /// inside one of the clamps?</summary>
    public bool ToolHits(Vector3 tip, float radius)
    {
        float top = Stock.Blank.BoundsM.Y + 0.03f;
        return tip.Y < top && _clamps.Any(c =>
            Mathf.Abs(tip.X - (c.Node.Position.X + c.Side * 0.025f)) < 0.045f + radius
            && Mathf.Abs(tip.Z - c.Node.Position.Z) < 0.015f + radius);
    }

    public string SheetLines
    {
        get
        {
            int tight = _clamps.Count(c => c.Nut.IsTight);
            string text = StockHolding.Mark(true) + "Kütük tablada" + (_onChips ? "  (TALAŞ ÜSTÜNE OTURDU)" : "") + "\n";
            text += StockHolding.Mark(Aligned) + System.FormattableString.Invariant($"Kütük X'e paralel (komparatör)  sapma {RunoutMm:+0.00;-0.00} mm\n");
            text += StockHolding.Mark(_clamps.Any(c => c.Side < 0) && _clamps.Any(c => c.Side > 0)) + $"İki uçta pabuç  ({_clamps.Count} pabuç)\n";
            text += StockHolding.Mark(HoldsFirmly && tight == _clamps.Count) + $"Pabuç somunları tam sıkı  {tight}/{_clamps.Count}\n";
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
        var body = StockHolding.Release(Stock, MachinedPart, WasCut);
        Stock = null;
        MachinedPart = null;
        GetParent().RemoveChild(this);
        QueueFree();
        return body;
    }

    public string Prompt(PlayerController player)
    {
        var tool = player.HeldTool?.Kind;
        if (player.Held is ClampItem)
            return SlotFor(ToLocal(player.LookPoint)) == null
                ? "Kütüğün bu kısmının altından T-kanal geçmiyor"
                : "[Tık] Pabucu kütüğün bu ucuna bağla";
        if (tool == HandToolKind.DialIndicator)
            return StockHolding.DialText(RunoutMm);
        if (tool == HandToolKind.Mallet)
            return AnyTight ? "Pabuçlar tam sıkı: kütük vurarak oynamaz"
                : System.FormattableString.Invariant($"[Tık] Kütüğün bu ucuna vur   sapma {RunoutMm:+0.00;-0.00} mm")
                  + (_clamps.Any(c => c.Nut.IsSnug) ? "  (ince ayar)" : "  (kaba ayar; ince ayar için somunları hafif sık)");
        if (player.Held == null)
            return _clamps.Count == 0 ? "[Tık] Kütüğü al" : $"Kütük {_clamps.Count} pabuçla bağlı (somunları T kollu alyanla sık / sök)";
        return null;
    }

    public void Use(PlayerController player)
    {
        if (player.Held is ClampItem)
        {
            if (AddClamp(ToLocal(player.LookPoint)))
                player.ReleaseHeld().QueueFree();
        }
        else if (player.HeldTool?.Kind == HandToolKind.Mallet)
            Tap(ToLocal(player.LookPoint).X);
        else if (player.Held == null && _clamps.Count == 0)
            player.Give(TakeStock());
    }
}
