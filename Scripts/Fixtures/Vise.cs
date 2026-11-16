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
/// A machine vise the player carries to the table, bolts down, and clamps
/// stock in. Local frame (metres): origin at the centre of the base's
/// underside, X along the jaws, Y up, the fixed jaw at the back (-Z) and the
/// screw toward the operator (+Z).
/// </summary>
public partial class Vise : Carryable, IInteractable, IStockFixture
{
    public const float BedTopY = 0.05f;
    public const float JawTopY = 0.095f;
    public const float JawWidth = 0.15f;
    public const float FixedJawFaceZ = -0.085f;
    public const float MaxOpeningMm = 150f;
    public const float ParallelHeight = 0.03f;
    private const float LiftWhenNotSeated = 0.00015f;

    public MillTable Table { get; private set; }
    public readonly List<Fastener> Bolts = new();
    public float OpeningMm { get; private set; } = 100f;
    public int Torque { get; private set; }
    public bool HasParallels { get; private set; }
    public bool Seated { get; private set; }
    public StockItem Stock { get; private set; }
    /// <summary>The stock in the jaws has had material removed.</summary>
    public bool WasCut { get; set; }
    /// <summary>How far the fixed jaw is out of parallel with machine X.</summary>
    public float YawDeg { get; private set; }
    public float RunoutMm => StockHolding.RunoutMm(YawDeg, JawWidth / WorldScale.MmToM);
    public bool Aligned => Mathf.Abs(RunoutMm) <= StockHolding.AlignedWithinMm;
    public bool HoldsFirmly => BoltsTight && StockClamped;
    public string LooseReason => BoltsTight
        ? "PARÇA FIRLADI: mengene sıkılmadan kesime girildi"
        : "PARÇA FIRLADI: mengene cıvataları tam sıkılmamıştı";
    /// <summary>Once machining starts the raw stock is replaced by this.</summary>
    public Node3D MachinedPart { get; private set; }

    public bool IsMounted => Table != null;
    public bool BoltsTight => Bolts.Count > 0 && Bolts.All(b => b.IsTight);
    public bool StockClamped => Stock != null && Torque >= 2;
    public override bool CanPickUp => !IsMounted || (Stock == null && !HasParallels && Bolts.All(b => !b.Present));

    private MeshInstance3D _movingJaw;
    private Node3D _parallels;

    public static Vise Create()
    {
        var vise = new Vise { Name = "Vise", DisplayName = "Mengene", Mass = 18f };
        var body = BodyParts.Mat(new Color(0.2f, 0.32f, 0.5f), 0.4f, 0.5f);
        BodyParts.Box(vise, "Base", new Vector3(0.16f, BedTopY, 0.28f), new Vector3(0f, BedTopY / 2f, 0.01f), body);
        BodyParts.Box(vise, "Ears", new Vector3(0.27f, 0.02f, 0.24f), new Vector3(0f, 0.01f, 0f), body);
        BodyParts.Box(vise, "FixedJaw", new Vector3(JawWidth, JawTopY - BedTopY, 0.03f),
            new Vector3(0f, (JawTopY + BedTopY) / 2f, FixedJawFaceZ - 0.015f), BodyParts.Steel);
        vise._movingJaw = BodyParts.Box(vise, "MovingJaw", new Vector3(JawWidth, JawTopY - BedTopY, 0.03f), Vector3.Zero, BodyParts.Steel);
        BodyParts.Cylinder(vise, "Screw", 0.012f, 0.06f, new Vector3(0f, 0.035f, 0.17f), Vector3.Back, BodyParts.DarkSteel);
        vise._parallels = new Node3D { Name = "Parallels", Visible = false };
        vise.AddChild(vise._parallels);
        vise.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.16f, JawTopY, 0.28f) }, Position = new Vector3(0f, JawTopY / 2f, 0.01f) });
        vise.Refresh();
        return vise;
    }

    private float MovingJawFaceZ => FixedJawFaceZ + OpeningMm * WorldScale.MmToM;

    private void Refresh()
    {
        _movingJaw.Position = new Vector3(0f, (JawTopY + BedTopY) / 2f, MovingJawFaceZ + 0.015f);
        _parallels.Visible = HasParallels;
        if (Stock != null)
            Stock.Transform = StockTransform();
    }

    /// <summary>Where the stock's centre sits: against the fixed jaw, on
    /// the parallels (or the bed), where along the jaws the player put it.</summary>
    private Transform3D StockTransform()
    {
        var bounds = Stock.Blank.BoundsM;
        float y = BedTopY + (HasParallels ? ParallelHeight : 0f) + bounds.Y / 2f + (Torque > 0 && !Seated ? LiftWhenNotSeated : 0f);
        return new Transform3D(Basis.Identity, new Vector3(_stockX, y, FixedJawFaceZ + bounds.Z / 2f));
    }

    private float _stockX;

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

    /// <summary>Sets the vise on the table at the given spot (table frame)
    /// and opens its bolt holes over the nearest T-slots. Set down by hand
    /// it is never square to the table: yawDeg null = a random small angle.</summary>
    public void MountOn(MillTable table, float x, float? yawDeg = null)
    {
        Table = table;
        Freeze = true;
        Reparent(table);
        YawDeg = yawDeg ?? StockHolding.HandPlacedYawDeg(0.6f);
        _mountX = x;
        _onChips = table.Dirty;
        ApplyMount();
        foreach (float slotZ in table.SlotsNear(-0.09f, 0.09f))
            foreach (float side in new[] { -1f, 1f })
            {
                var bolt = new Fastener { Name = "HoldDown", Position = new Vector3(side * 0.115f, 0.02f, slotZ) };
                AddChild(bolt);
                Bolts.Add(bolt);
            }
    }

    private float _mountX;
    private bool _onChips;

    // A vise set down on chips does not sit flat.
    private void ApplyMount()
    {
        Transform = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(YawDeg)) * new Basis(Vector3.Right, _onChips ? Mathf.DegToRad(0.15f) : 0f),
            new Vector3(_mountX, 0f, 0f));
    }

    /// <summary>A mallet tap on one end of the vise; it only moves while
    /// the bolts are not fully tight, and only a little once they are snug.</summary>
    public void Tap(float lookLocalX)
    {
        if (BoltsTight)
            return;
        YawDeg = StockHolding.Tap(YawDeg, lookLocalX, Bolts.Any(b => b.IsSnug));
        ApplyMount();
    }

    public string SheetLines
    {
        get
        {
            int tight = Bolts.Count(b => b.IsTight);
            string text = StockHolding.Mark(true) + "Mengene tablada" + (_onChips ? "  (TALAŞ ÜSTÜNE OTURDU)" : "") + "\n";
            text += StockHolding.Mark(Aligned) + System.FormattableString.Invariant($"Mengene hizalı (komparatör)  sapma {RunoutMm:+0.00;-0.00} mm\n");
            text += StockHolding.Mark(BoltsTight) + $"Cıvatalar tam sıkı  {tight}/{Bolts.Count}\n";
            text += StockHolding.Mark(HasParallels) + "Paraleller\n";
            text += StockHolding.Mark(Stock != null) + "Kütük mengenede\n";
            text += StockHolding.Mark(Torque >= 3) + $"Mengene sıkıldı  {Torque}/3\n";
            text += StockHolding.Mark(Seated) + "Çekiçle oturtuldu\n";
            return text;
        }
    }

    public string Prompt(PlayerController player)
    {
        if (!IsMounted)
            return null;
        var tool = player.HeldTool?.Kind;
        float needed = Stock != null ? Stock.Blank.SizeMm.Y : 0f;

        if (tool == HandToolKind.DialIndicator)
            return StockHolding.DialText(RunoutMm);
        if (tool == HandToolKind.Mallet && Stock == null)
            return BoltsTight ? "Cıvatalar tam sıkı: mengene vurarak oynamaz"
                : System.FormattableString.Invariant($"[Tık] Mengenenin bu ucuna vur   sapma {RunoutMm:+0.00;-0.00} mm")
                  + (Bolts.Any(b => b.IsSnug) ? "  (ince ayar)" : "  (kaba ayar; ince ayar için cıvataları hafif sık)");

        if (tool == HandToolKind.ViseHandle)
            return Stock != null && OpeningMm <= needed
                ? $"[Tekerlek] Mengeneyi sık / gevşet   sıkma {Torque}/3"
                : $"[Tekerlek] Çeneyi aç / kapat   açıklık {OpeningMm:0} mm";
        if (player.Held is StockItem item && Stock == null)
        {
            if (item.Blank.Shape != StockShape.Block)
                return "Mengene yalnızca blok kütük tutar";
            if (item.Blank.SizeMm.Y > MaxOpeningMm)
                return $"Kütük mengeneye sığmaz (en fazla {MaxOpeningMm:0} mm)";
            return OpeningMm >= item.Blank.SizeMm.Y + 1f
                ? "[Tık] Kütüğü mengeneye koy"
                : $"Çene açıklığı yetmiyor: {OpeningMm:0} mm, kütük {item.Blank.SizeMm.Y:0} mm (mengene koluyla aç)";
        }
        if (tool == HandToolKind.Parallels && !HasParallels && Stock == null)
            return "[Tık] Paralelleri yerleştir";
        if (tool == HandToolKind.Mallet && Stock != null)
            return Torque == 0 ? "Önce mengeneyi sık, sonra çekiçle oturt" : Seated ? "Kütük oturdu" : "[Tık] Çekiçle vurarak kütüğü oturt";
        if (player.Held == null && Stock != null)
            return OpeningMm > needed ? "[Tık] Parçayı al" : $"Mengene sıkılı ({Torque}/3). Parçayı almak için mengene koluyla gevşet";
        if (player.Held == null && HasParallels)
            return "[Tık] Paralelleri al";
        if (Stock == null)
            return $"Mengene   açıklık {OpeningMm:0} mm" + (HasParallels ? ", paralelli" : ", paralelsiz");
        return null;
    }

    public void Use(PlayerController player)
    {
        if (!IsMounted)
            return;
        var tool = player.HeldTool?.Kind;
        if (player.Held is StockItem item && Stock == null && item.Blank.Shape == StockShape.Block
            && item.Blank.SizeMm.Y <= MaxOpeningMm && OpeningMm >= item.Blank.SizeMm.Y + 1f)
        {
            player.ReleaseHeld();
            float reach = Mathf.Max(0f, item.Blank.BoundsM.X / 2f - 0.02f);
            PlaceStock(item, Mathf.Clamp(ToLocal(player.LookPoint).X, -reach - JawWidth / 2f + 0.02f, reach + JawWidth / 2f - 0.02f));
        }
        else if (tool == HandToolKind.Parallels && !HasParallels && Stock == null)
        {
            player.ReleaseHeld().QueueFree();
            PlaceParallels();
        }
        else if (tool == HandToolKind.Mallet && Stock == null)
            Tap(ToLocal(player.LookPoint).X);
        else if (tool == HandToolKind.Mallet)
            SeatWithMallet();
        else if (player.Held == null && Stock != null && OpeningMm > Stock.Blank.SizeMm.Y)
            player.Give(TakeStock());
        else if (player.Held == null && HasParallels && Stock == null)
        {
            HasParallels = false;
            var parallels = HandTool.Create(HandToolKind.Parallels);
            GetTree().CurrentScene.AddChild(parallels);
            player.Give(parallels);
        }
        Refresh();
    }

    public void Turn(PlayerController player, int steps)
    {
        if (IsMounted && player.HeldTool?.Kind == HandToolKind.ViseHandle)
            TurnScrew(steps);
    }

    /// <summary>One wheel notch on the screw: +1 closes / tightens.</summary>
    public void TurnScrew(int steps)
    {
        float contact = Stock != null ? Stock.Blank.SizeMm.Y : 0f;
        if (steps > 0)
        {
            if (OpeningMm > contact)
                OpeningMm = Mathf.Max(contact, OpeningMm - 2f);
            else if (Stock != null)
                Torque = Mathf.Min(3, Torque + 1);
        }
        else if (Torque > 0)
        {
            Torque--;
            // Loosening lets the part ride up again.
            if (Torque == 0)
                Seated = false;
        }
        else
            OpeningMm = Mathf.Min(MaxOpeningMm, OpeningMm + 2f);
        if (HasParallels)
            _parallels.GetChild<Node3D>(1).Position = new Vector3(0f, BedTopY + ParallelHeight / 2f, MovingJawFaceZ - 0.004f);
        Refresh();
    }

    public void PlaceStock(StockItem item, float x)
    {
        Stock = item;
        _stockX = x;
        Seated = false;
        WasCut = false;
        item.Fixed = true;
        item.Freeze = true;
        item.Reparent(this);
        Refresh();
    }

    public void PlaceParallels()
    {
        HasParallels = true;
        BodyParts.Clear(_parallels);
        foreach (float z in new[] { FixedJawFaceZ + 0.004f, MovingJawFaceZ - 0.004f })
            BodyParts.Box(_parallels, "Parallel", new Vector3(JawWidth, ParallelHeight, 0.004f),
                new Vector3(0f, BedTopY + ParallelHeight / 2f, z), BodyParts.Steel);
        Refresh();
    }

    public void SeatWithMallet()
    {
        if (Stock != null && Torque > 0)
            Seated = true;
        Refresh();
    }

    /// <summary>Machining has started: the raw stock's look is replaced by
    /// the cut surface, which stays in the vise in its place.</summary>
    public void ShowMachined(Node3D machined)
    {
        MachinedPart = machined;
        StockHolding.ShowRaw(Stock, false);
    }

    /// <summary>Takes whatever is in the jaws out: the untouched stock, or
    /// the machined part as a new carryable object.</summary>
    public RigidBody3D TakeStock()
    {
        var stock = Stock;
        var machined = MachinedPart;
        Stock = null;
        MachinedPart = null;
        Torque = 0;
        Seated = false;
        return StockHolding.Release(stock, machined, WasCut);
    }
}
