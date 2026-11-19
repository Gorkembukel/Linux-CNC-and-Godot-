using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// A chuck the player works with the chuck key: key in, wheel to open,
/// close and tighten the jaws, key OUT again. Holds one round bar. Built
/// from its own <see cref="ChuckModel"/>; on a lathe it comes off once its
/// cam locks are undone, and its jaws go in and out one by one. Local
/// frame (metres): X along the spindle axis pointing out of the chuck, origin
/// at the front face of the jaws on the centreline.
/// </summary>
[Tool]
public partial class JawChuck : StaticBody3D, IInteractable, IStockFixture, IMeasurable, ISawable, IThreaded
{
    private const float M = WorldScale.MmToM;
    private const float JawHeight = 0.022f;

    public ChuckGrip Grip { get; private set; }
    public StockSeat Seat { get; } = new();
    public StockItem Stock => Seat.Stock;
    public float RadiusM { get; private set; }

    public bool HoldsFirmly => Grip.HoldsFirmly;
    public string LooseReason => Grip.Torque >= 2
        ? "PARÇA FIRLADI: kütük çenelerin içinde çok az tutuluyordu (en az 15 mm)"
        : "PARÇA FIRLADI: ayna sıkılmadan kesime girildi";

    /// <summary>The bar was put in, moved or taken out.</summary>
    public System.Action Changed;
    /// <summary>What comes out of the jaws instead of the raw bar once it
    /// has been machined; null result = hand back the raw bar.</summary>
    public System.Func<RigidBody3D> TakeMachined;
    /// <summary>Gauge reading for a point (global) on the bar; set by the
    /// lathe. On an indexer the bar is measured like any milled stock.</summary>
    public System.Func<HandToolKind, Vector3, string> Measure;

    public string Reading(HandToolKind gauge, Vector3 globalPoint) =>
        !Grip.HasBar ? null : Measure != null ? Measure(gauge, globalPoint) : Stock.Reading(gauge, globalPoint);

    /// <summary>Who cuts the bar when a hand saw is put to it; set by the
    /// lathe (an indexer's chuck has none).</summary>
    public ISawable Saw;

    /// <summary>What the lathe does first with a click, a wheel notch or the
    /// prompt (a dial indicator on the bar, a mallet tap); true / text when it
    /// took it.</summary>
    public System.Func<PlayerController, bool> UseWith;
    public System.Func<int, bool> TurnWith;
    public System.Func<PlayerController, string> PromptWith;

    public MaterialKind SawMaterial => Saw?.SawMaterial ?? MaterialKind.Steel;

    public SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        why = null;
        return Grip.HasBar ? Saw?.PlanCut(globalPoint, angleDeg, out why) : null;
    }

    public void FinishCut(SawCut cut) => Saw?.FinishCut(cut);

    /// <summary>Who answers for the bar's thread; set by the lathe.</summary>
    public IThreaded Threads;

    public ThreadSite? ThreadAt(Vector3 globalPoint, out string why)
    {
        why = null;
        return Grip.HasBar ? Threads?.ThreadAt(globalPoint, out why) : null;
    }

    /// <summary>Cam locks holding the chuck to the spindle nose.</summary>
    public const int CamCount = 3;

    /// <summary>The chuck's own sizes.</summary>
    public ChuckModel Model { get; private set; }
    /// <summary>Cam locks done up: all of them before the spindle may turn.</summary>
    public int CamsLocked { get; private set; } = CamCount;
    public bool CamsDone => CamsLocked >= CamCount;
    /// <summary>Why the lathe will not take another chuck, null when it
    /// will; set by the lathe (an indexer's chuck stays on).</summary>
    public System.Func<ChuckModel, string> WhyNotMount;
    /// <summary>Another chuck went on: the lathe moves its work zone.</summary>
    public System.Action Refitted;
    /// <summary>The spindle is turning (cam locks are not touched then); set by the lathe.</summary>
    public System.Func<bool> SpindleTurning;

    private Node3D _rotor, _key, _collet;
    private readonly List<(MeshInstance3D Mesh, int Slot)> _jaws = new();
    private CollisionShape3D _bodyShape, _barShape;
    private float _bodyLengthM, _jawDepthM;

    /// <summary>A three-jaw chuck sized from the older per-machine settings.</summary>
    public static JawChuck Create(float diameterMm, int jawCount, float jawDepthMm, float boreDepthMm)
    {
        var model = ChuckModel.ThreeJaw(diameterMm, jawCount, jawDepthMm, 4000f);
        return Create(model, new ChuckGrip(model.Dims(boreDepthMm)));
    }

    public static JawChuck Create(ChuckModel model, ChuckGrip grip)
    {
        var chuck = new JawChuck { Name = "Chuck", CollisionLayer = Fastener.InteractionLayer, CollisionMask = 0 };
        chuck._rotor = new Node3D { Name = "Rotor" };
        chuck.AddChild(chuck._rotor);
        chuck._bodyShape = new CollisionShape3D { Shape = new CylinderShape3D(), RotationDegrees = new Vector3(0f, 0f, 90f) };
        chuck.AddChild(chuck._bodyShape);
        chuck._barShape = new CollisionShape3D { Shape = new CylinderShape3D(), RotationDegrees = new Vector3(0f, 0f, 90f), Disabled = true };
        chuck.AddChild(chuck._barShape);
        chuck.Fit(model, grip);
        return chuck;
    }

    /// <summary>Builds this chuck's body, jaws or collet nose and key from
    /// its own sizes; the front face of the jaws stays at the origin.</summary>
    private void Fit(ChuckModel model, ChuckGrip grip)
    {
        Model = model;
        Grip = grip;
        RadiusM = model.DiameterMm / 2f * M;
        _jawDepthM = model.JawDepthMm * M;
        _bodyLengthM = model.BodyLengthMm * M;
        foreach (Node child in _rotor.GetChildren())
        {
            _rotor.RemoveChild(child);
            child.QueueFree();
        }
        _jaws.Clear();

        float bodyCentre = -_jawDepthM * 0.7f - _bodyLengthM / 2f;
        BodyParts.Cylinder(_rotor, "Body", RadiusM, _bodyLengthM, new Vector3(bodyCentre, 0f, 0f), Vector3.Right, BodyParts.Steel);
        for (int i = 0; i < grip.Jaws.SlotCount; i++)
        {
            var arm = new Node3D { Name = "Jaw", RotationDegrees = new Vector3(i * 360f / grip.Jaws.SlotCount, 0f, 0f) };
            _rotor.AddChild(arm);
            _jaws.Add((BodyParts.Box(arm, "JawBlock", new Vector3(_jawDepthM * 0.7f, RadiusM * 0.45f, JawHeight), Vector3.Zero, BodyParts.DarkSteel), i));
        }
        _collet = null;
        if (grip.IsCollet)
        {
            BodyParts.Cylinder(_rotor, "Nose", RadiusM * 0.6f, _jawDepthM * 0.7f, new Vector3(-_jawDepthM * 0.35f, 0f, 0f), Vector3.Right,
                BodyParts.DarkSteel, topRadius: RadiusM * 0.45f);
            _collet = new Node3D { Name = "Collet" };
            _rotor.AddChild(_collet);
            BodyParts.Cylinder(_collet, "Collet", RadiusM * 0.3f, 0.004f, new Vector3(0.002f, 0f, 0f), Vector3.Right, BodyParts.Steel);
        }
        _key = new Node3D { Name = "Key", Position = new Vector3(bodyCentre, RadiusM, 0f), Visible = false };
        _rotor.AddChild(_key);
        BodyParts.Cylinder(_key, "Shaft", 0.007f, 0.12f, new Vector3(0f, 0.05f, 0f), Vector3.Up, BodyParts.Steel);
        BodyParts.Cylinder(_key, "Handle", 0.006f, 0.14f, new Vector3(0f, 0.11f, 0f), Vector3.Right, BodyParts.Steel);

        float length = _bodyLengthM + _jawDepthM * 0.7f;
        ((CylinderShape3D)_bodyShape.Shape).Radius = RadiusM;
        ((CylinderShape3D)_bodyShape.Shape).Height = length;
        _bodyShape.Position = new Vector3(-length / 2f, 0f, 0f);
        Refresh();
    }

    private void Refresh()
    {
        var g = Grip;
        float inner = g.OpeningMm / 2f * M, jawRadial = RadiusM * 0.45f;
        foreach (var (mesh, slot) in _jaws)
        {
            if (g.Jaws.Slot(slot) is not SeatedJaw seated)
            {
                mesh.Visible = false;
                continue;
            }
            mesh.Visible = true;
            // A jaw caught out of turn sits further out than the others.
            float off = g.Jaws.ErrorMm(slot) * M;
            float centre = (g.Inside ? inner - jawRadial / 2f : inner + jawRadial / 2f) + off;
            mesh.Position = new Vector3(-g.JawDepthMm * M * 0.35f, Mathf.Min(centre, RadiusM - jawRadial / 2f), 0f);
            mesh.MaterialOverride = BodyParts.Mat(JawItem.ColorOf(seated.Kind), seated.Kind == JawKind.Soft ? 0.5f : 0.7f, 0.45f);
        }
        if (_collet != null)
            _collet.Visible = g.Jaws.ColletMm != null;
        _key.Visible = g.KeyIn;

        _barShape.Disabled = !g.HasBar;
        if (!g.HasBar)
            return;
        float stickOut = Mathf.Max(0.002f, g.StickOutMm * M);
        var shape = (CylinderShape3D)_barShape.Shape;
        shape.Radius = g.BarDiameterMm / 2f * M;
        shape.Height = stickOut;
        _barShape.Position = new Vector3(stickOut / 2f, 0f, 0f);
        if (Stock != null)
            Stock.Transform = new Transform3D(Basis.Identity, new Vector3((g.StickOutMm - Stock.Blank.LengthMm / 2f) * M, 0f, 0f));
    }

    /// <summary>Why the chuck cannot come off now, or null.</summary>
    private string WhyNotSwap(ChuckModel incoming) =>
        WhyNotMount == null ? "Bu ayna sökülmez"
        : Grip.HasBar ? "Ayna değiştirmek için önce parçayı çıkar"
        : Grip.KeyIn ? "Önce ayna anahtarını çıkar"
        : CamsLocked > 0 ? $"Ayna fener miline kilitli: kam anahtarıyla kam kilitlerini çöz ({CamsLocked}/{CamCount} kilitli)"
        : WhyNotMount(incoming);

    /// <summary>The carried chuck goes on the nose, this one (with whatever
    /// jaws are in it) comes off into the player's hands.</summary>
    private void SwapChuck(PlayerController player, ChuckItem item)
    {
        if (WhyNotSwap(item.Model) is string why)
        {
            PlayerController.Notify(this, why);
            return;
        }
        var oldModel = Model;
        var oldGrip = Grip;
        player.ReleaseHeld();
        Fit(item.Model, item.Grip);
        item.QueueFree();
        var removed = ChuckItem.Create(oldModel, oldGrip);
        GetTree().CurrentScene.AddChild(removed);
        player.Give(removed);
        CamsLocked = 0;
        Refitted?.Invoke();
        Changed?.Invoke();
        PlayerController.Notify(this, $"{Model.DisplayName} fener miline oturdu: kam anahtarıyla {CamCount} kam kilidini kilitle");
    }

    private void TurnCams(int steps)
    {
        CamsLocked = Mathf.Clamp(CamsLocked + Mathf.Sign(steps), 0, CamCount);
        PlayerController.Notify(this, CamsDone ? $"Kam kilitleri tamam ({CamCount}/{CamCount}): ayna fener miline kilitli"
            : $"Kam kilidi {CamsLocked}/{CamCount}");
    }

    /// <summary>A jaw goes into the slot the scroll is at.</summary>
    private void InstallJaw(PlayerController player, JawItem jaw)
    {
        int slot = Grip.Jaws.OpenSlot ?? -1;
        if (Grip.InstallJaw(jaw.Kind, jaw.Number, jaw.MadeFor) is string why)
        {
            PlayerController.Notify(this, why);
            return;
        }
        player.ReleaseHeld().QueueFree();
        Refresh();
        string next = Grip.Jaws.Complete ? "Çeneler tamam: anahtarla kapatıp ortada buluştuklarına bak"
            : Grip.Jaws.Independent ? "Sıradaki çeneyi tak"
            : "Anahtarı çevir: spiralin başı sıradaki yuvaya gelsin";
        PlayerController.Notify(this, $"{jaw.DisplayName} {slot + 1}. yuvaya girdi. {next}");
    }

    private void InstallCollet(PlayerController player, ColletItem collet)
    {
        if (Grip.InstallCollet(collet.SizeMm) is string why)
        {
            PlayerController.Notify(this, why);
            return;
        }
        player.ReleaseHeld().QueueFree();
        Refresh();
    }

    /// <summary>Turns the jaws and the key with the spindle.</summary>
    public void Spin(float degrees) => _rotor.RotateX(Mathf.DegToRad(degrees));

    /// <summary>The raw bar's own look, hidden while something else draws the part.</summary>
    public void ShowRawStock(bool visible) => Seat.ShowRaw(visible);

    public RigidBody3D TakeStock() => TakeBar();

    public string ToolCrash(Vector3 tip, float radius) =>
        tip.X < 0f && new Vector2(tip.Y, tip.Z).Length() < RadiusM + radius ? "ÇARPMA: takım aynaya girdi" : null;

    private string JawLine
    {
        get
        {
            var g = Grip;
            if (g.IsCollet)
                return g.Jaws.ColletMm is float c ? System.FormattableString.Invariant($"Pens Ø{c:0} ({c - 1f:0}-{c:0} mm)") : "Pens yok";
            string set = JawItem.SetNameOf(g.JawSet);
            if (g.JawSet == JawKind.Soft && g.SoftBoredForMm is float bored)
                set += System.FormattableString.Invariant($" (Ø{bored:0.##} için tornalı)");
            return g.Jaws.Complete ? set : $"Çeneler {g.Jaws.Seated}/{g.Jaws.SlotCount} takılı";
        }
    }

    public string SheetLines
    {
        get
        {
            var g = Grip;
            string text = $"    Ayna: {Model.DisplayName}, {JawLine}\n";
            if (WhyNotMount != null)
                text += Sheet.Mark(CamsDone) + $"Kam kilitleri {CamsLocked}/{CamCount}\n";
            text += Sheet.Mark(g.HasBar) + "Kütük aynada"
                + (g.HasBar ? System.FormattableString.Invariant($"  Ø{g.BarDiameterMm:0.#} x {g.BarLengthMm:0.#}") : "") + "\n";
            if (!g.HasBar)
                return text + (!g.Jaws.Ready
                    ? g.IsCollet ? "    Çubuğun ölçüsündeki pensi tak\n"
                    : "    Anahtarı tak, spirali çevir: başı yuvaya\n    gelince o numaralı çeneyi tak (1, 2, 3...)\n"
                    : "    Ayna anahtarını tak, çeneleri aç,\n    yuvarlak çubuğu aynaya sür\n");
            if (g.Inside)
                text += System.FormattableString.Invariant($"    İçten bağlı: çeneler Ø{g.GripDiameterMm:0.#} deliğe açıldı\n");
            text += Sheet.Mark(g.GripLengthMm >= ChuckGrip.MinGripMm)
                + System.FormattableString.Invariant($"Çıkıntı {g.StickOutMm:0} mm, çenede {g.GripLengthMm:0} mm\n");
            if (g.Slenderness > Machining.CuttingLoad.SlenderWorkRatio)
                text += System.FormattableString.Invariant($"[!] Çıkıntı çapın {g.Slenderness:0.#} katı: tırlar, kısa bağla\n");
            text += Sheet.Mark(g.Torque >= ChuckGrip.FullTorque) + $"Ayna sıkıldı  {g.Torque}/{ChuckGrip.FullTorque}\n";
            text += Sheet.Mark(!g.KeyIn) + "Anahtar aynadan çıkarıldı\n";
            return text;
        }
    }

    public void InsertKey()
    {
        Grip.KeyIn = true;
        Refresh();
    }

    /// <summary>The key as a loose tool again, where it sat in the chuck.</summary>
    public HandTool TakeKey()
    {
        Grip.KeyIn = false;
        var key = HandTool.Create(HandToolKind.ChuckKey);
        GetTree().CurrentScene.AddChild(key);
        key.GlobalPosition = _key.GlobalPosition + _key.GlobalBasis.Y * 0.08f;
        Refresh();
        return key;
    }

    /// <summary>One notch of the key; a jaw that comes off the scroll goes
    /// to the player's hand (and the key stops while that hand is full).</summary>
    public void TurnKey(int steps, PlayerController player = null)
    {
        var released = Grip.TurnKey(steps, canTakeJaw: player != null && player.Held == null);
        if (released is SeatedJaw jaw)
        {
            var item = JawItem.Create(jaw.Kind, jaw.Number, Model.Kind);
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = GlobalPosition;
            player.Give(item);
            PlayerController.Notify(this, $"{jaw.Number} numaralı çene spiralden kurtuldu, elinde");
        }
        Refresh();
    }

    public bool InsertBar(StockItem item, Basis? turn = null)
    {
        var t = turn ?? Basis.Identity;
        if (!Grip.Insert(GripSizeMm(item, t), SquareTurn.Extents(t, item.Blank.BoundsM).X / WorldScale.MmToM))
            return false;
        Seat.Put(item, this, new Transform3D(t, Vector3.Zero));
        if (item.Burred)
            Grip.AddRunout(0.03f);
        Refresh();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts a part on the jaws held by its bore.</summary>
    public bool InsertBarInside(StockItem item, float boreMm, float boreDepthMm)
    {
        if (!Grip.InsertInside(boreMm, boreDepthMm, GripSizeMm(item, Basis.Identity), SquareTurn.Extents(Basis.Identity, item.Blank.BoundsM).X / WorldScale.MmToM))
            return false;
        Seat.Put(item, this, Transform3D.Identity);
        Refresh();
        Changed?.Invoke();
        return true;
    }

    public void SlideBar(float mm)
    {
        Grip.Slide(mm);
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>The bar got shorter (a piece was parted off).</summary>
    public void SetBarLength(float lengthMm)
    {
        Grip.SetLength(lengthMm);
        Refresh();
    }

    /// <summary>Takes whatever is in the jaws out: the raw bar, or the
    /// machined part as a new object.</summary>
    public RigidBody3D TakeBar()
    {
        RigidBody3D result = TakeMachined?.Invoke();
        Grip.Remove();
        if (result != null)
            Seat.Discard();
        else
            result = Seat.Take();
        Refresh();
        Changed?.Invoke();
        return result;
    }

    public string Prompt(PlayerController player)
    {
        if (PromptWith?.Invoke(player) is string extra)
            return extra;
        var tool = player.HeldTool?.Kind;
        var g = Grip;
        if (tool == HandToolKind.ChuckKey)
            return "[Tık] Anahtarı aynaya tak";
        if (tool == HandToolKind.CamKey)
            return WhyNotMount == null ? null
                : $"Kam kilitleri {CamsLocked}/{CamCount}   [Tekerlek ileri] kilitle   [Tekerlek geri] çöz";
        if (player.Held is ChuckItem other)
            return WhyNotSwap(other.Model) ?? $"[Tık] {other.DisplayName} tak ({Model.DisplayName} sök)";
        if (player.Held is JawItem jawItem)
            return g.IsCollet ? "Pens aynasına çene takılmaz"
                : jawItem.MadeFor != Model.Kind ? "Bu çene bu aynanın değil"
                : g.HasBar ? "Çene takmak için önce parçayı çıkar"
                : !g.KeyIn ? "Çeneler ayna anahtarıyla takılır: önce anahtarı tak"
                : g.Jaws.Set is JawKind set && set != jawItem.Kind ? "Farklı takım çeneler karıştırılmaz: önce takılı olanları sök"
                : g.Jaws.OpenSlot is int slot ? $"[Tık] {jawItem.DisplayName} → {slot + 1}. yuvaya tak"
                : g.Jaws.Complete ? "Bütün yuvalar dolu"
                : $"Spiralin başı {g.Jaws.StartSlot + 1}. yuvada, orası dolu: anahtarı çevir";
        if (player.Held is ColletItem colletItem)
            return !g.IsCollet ? "Bu ayna pens almaz"
                : g.Jaws.ColletMm != null ? "Aynada zaten bir pens var: önce onu çıkar"
                : $"[Tık] {colletItem.DisplayName} tak";
        if (player.Held is StockItem held)
        {
            if (g.HasBar)
                return "Aynada zaten bir kütük var";
            if (MilledBefore(held))
                return "Frezede işlenmiş parça aynaya takılmaz";
            if (Placement(player, held) is not var (turn, reversed))
                return "Parçayı ayna ekseni doğrultusunda tut ([Z/X] ya da orta tuşla çevir)";
            float size = GripSizeMm(held, turn, reversed);
            if (!g.IsCollet && g.Jaws.Complete && g.JawSet == JawKind.Soft && !g.SoftJawsFit(size) && size <= g.MaxGripDiameterMm)
                return System.FormattableString.Invariant($"[Tık] Yumuşak çeneleri bu parçaya göre tornala (Ø{size:0.##})");
            if (!g.CanInsert(size) && BoreAt(held, reversed) is var (bore, depth))
            {
                if (g.CanInsertInside(bore))
                    return System.FormattableString.Invariant($"[Tık] Aynaya içten bağla (delik Ø{bore:0.#}, {depth:0} mm derin)");
                if (g.WhyNotInside(bore) is null)
                    return System.FormattableString.Invariant($"İçten bağlamak için çeneleri Ø{bore - 1f:0}'in altına kapat (şimdi Ø{g.OpeningMm:0}), ya da dıştan bağlamak için aç");
            }
            if (g.WhyNot(size) is string why)
                return why;
            string what = held.Blank.Shape == StockShape.Block
                ? System.FormattableString.Invariant($"  (eksen boyunca {SquareTurn.Extents(turn, held.Blank.BoundsM).X / WorldScale.MmToM:0} mm, çeneler {size:0} mm)")
                : held.Turned != null ? (reversed ? "  (+X ucu aynaya)" : "  (-X ucu aynaya)") : "";
            return g.CanInsert(size)
                ? "[Tık] Aynaya sür" + what
                : g.IsCollet ? "Pens sıkılı: anahtarla gevşet"
                : $"Çeneler dar: {g.OpeningMm:0} mm, tutulan kısım {size:0.#} mm (ayna anahtarıyla aç)";
        }
        if (player.Held != null)
            return null;
        if (g.KeyIn)
            return KeyPrompt + "   [Tık] Anahtarı çıkar";
        if (g.HasBar)
            return g.Torque > 0
                ? $"Ayna sıkılı ({g.Torque}/{ChuckGrip.FullTorque}), çıkıntı {g.StickOutMm:0} mm. Gevşetmek için ayna anahtarını tak"
                : $"[Tekerlek] Kütüğü it / çek   çıkıntı {g.StickOutMm:0} mm   [Tık] Kütüğü al";
        if (g.IsCollet && g.Jaws.ColletMm != null)
            return $"{Model.DisplayName}: {JawLine}   [Tık] Pensi çıkar";
        return $"{Model.DisplayName}: {JawLine}" + (g.IsCollet ? "" : $", açıklık Ø{g.OpeningMm:0} mm (ayna anahtarı gerekli)");
    }

    /// <summary>What the key does now.</summary>
    private string KeyPrompt
    {
        get
        {
            var g = Grip;
            if (g.IsCollet)
                return g.HasBar ? $"[Tekerlek] Pensi sık / gevşet   sıkma {g.Torque}/{ChuckGrip.FullTorque}" : "Pens aynası: önce çubuğu sür";
            if (!g.Jaws.Complete)
            {
                int start = g.Jaws.StartSlot + 1;
                string at = g.Jaws.Independent ? $"{g.Jaws.Seated}/{g.Jaws.SlotCount} çene takılı"
                    : g.Jaws.Slot(start - 1) == null ? $"spiralin başı {start}. yuvada: oraya {start} numaralı çene"
                    : $"spiralin başı {start}. yuvada (dolu)";
                return $"[Tekerlek] Spirali çevir ({at}; geri: çeneleri söker)";
            }
            if (g.AtContact)
                return $"[Tekerlek] Aynayı sık / gevşet   sıkma {g.Torque}/{ChuckGrip.FullTorque}";
            if (!g.HasBar && g.OpeningMm <= ChuckGrip.JawStepMm && !g.Jaws.MeetAtCentre)
                return "[Tekerlek] Çeneler ortada buluşmuyor: biri sırasız ya da geç takılmış (geri çevirip sök, sırayla tak)";
            return $"[Tekerlek] Çeneleri aç / kapat   Ø{g.OpeningMm:0} mm" + (g.FullyOpen ? " (tam açık: geri çevirmek çeneleri söker)" : "");
        }
    }

    public void Use(PlayerController player)
    {
        if (UseWith?.Invoke(player) == true)
            return;
        if (player.HeldTool?.Kind == HandToolKind.ChuckKey)
        {
            player.ReleaseHeld().QueueFree();
            InsertKey();
        }
        else if (player.HeldTool?.Kind == HandToolKind.CamKey && WhyNotMount != null)
            TurnCams(1);
        else if (player.Held is JawItem jaw)
            InstallJaw(player, jaw);
        else if (player.Held is ColletItem collet)
            InstallCollet(player, collet);
        else if (player.Held is ChuckItem chuck)
            SwapChuck(player, chuck);
        else if (player.Held is StockItem bar && !Grip.IsCollet && Grip.Jaws.Complete && Grip.JawSet == JawKind.Soft && !Grip.HasBar && !MilledBefore(bar)
                 && Placement(player, bar) is var (barTurn, barReversed) && GripSizeMm(bar, barTurn, barReversed) is var size
                 && !Grip.SoftJawsFit(size) && Grip.BoreSoftJaws(size))
        {
            PlayerController.Notify(this, System.FormattableString.Invariant($"Yumuşak çeneler Ø{size:0.##} için tornalandı: parça artık neredeyse salgısız oturur"));
            Refresh();
        }
        else if (player.Held is StockItem item && !MilledBefore(item) && !Grip.HasBar
                 && Placement(player, item) is var (turn, reversed) && Grip.CanInsert(GripSizeMm(item, turn, reversed)))
        {
            player.ReleaseHeld();
            if (reversed)
                item.ReverseTurned();
            InsertBar(item, turn);
        }
        else if (player.Held is StockItem hollow && !MilledBefore(hollow) && !Grip.HasBar
                 && Placement(player, hollow) is var (_, hollowReversed)
                 && BoreAt(hollow, hollowReversed) is var (bore, depth) && Grip.CanInsertInside(bore))
        {
            player.ReleaseHeld();
            if (hollowReversed)
                hollow.ReverseTurned();
            InsertBarInside(hollow, bore, depth);
        }
        else if (player.Held == null && Grip.KeyIn)
            player.Give(TakeKey());
        else if (player.Held == null && Grip.HasBar && Grip.Torque == 0)
            player.Give(TakeBar());
        else if (player.Held == null && Grip.IsCollet && Grip.TakeCollet() is float colletSize)
        {
            var taken = ColletItem.Create(colletSize);
            GetTree().CurrentScene.AddChild(taken);
            taken.GlobalPosition = GlobalPosition;
            player.Give(taken);
            Refresh();
        }
    }

    /// <summary>How the carried stock goes in, or null when it cannot. A
    /// raw block goes in however it is held (whatever lies along the spindle
    /// is its length); a bar or a turned part must lie along the spindle,
    /// and `reversed` says its +X end goes in first.</summary>
    private (Basis Turn, bool Reversed)? Placement(PlayerController player, StockItem item)
    {
        var turn = player.HeldTurnIn(this);
        if (item.Blank.Shape == StockShape.Block && item.Turned == null)
            return (turn, false);
        float along = (turn * Vector3.Right).X;
        return Mathf.Abs(along) < 0.5f ? null : (Basis.Identity, along < 0f);
    }

    /// <summary>What the jaws close on: a bar's diameter, a block's larger
    /// side across the axis, a turned part's end that goes in.</summary>
    private static float GripSizeMm(StockItem item, Basis turn, bool reversed = false)
    {
        if (item.Turned is { } t)
            return 2f * Material.LatheSection.Reach(reversed ? t.RadiiMm[^1] : t.RadiiMm[0], t.SquareHalfMm);
        if (item.Blank.Shape == StockShape.Block)
        {
            var across = SquareTurn.Extents(turn, item.Blank.BoundsM) / WorldScale.MmToM;
            return Mathf.Max(across.Y, across.Z);
        }
        return item.Blank.DiameterMm;
    }

    /// <summary>The bore at the end of a turned part that goes in (diameter,
    /// depth), or null when that end is solid.</summary>
    private static (float BoreMm, float DepthMm)? BoreAt(StockItem item, bool reversed)
    {
        if (item.Turned is not { } t || t.InnerMm.Length == 0)
            return null;
        int end = reversed ? t.InnerMm.Length - 1 : 0, step = reversed ? -1 : 1;
        float bore = t.InnerMm[end];
        if (bore <= 0f)
            return null;
        int count = 0;
        for (int i = end; i >= 0 && i < t.InnerMm.Length && t.InnerMm[i] >= bore - 0.05f; i += step)
            count++;
        return (2f * bore, count * t.StepMm);
    }

    private static bool MilledBefore(StockItem item) => item.GetChildren().OfType<VoxelStockVisual>().Any();

    public void Turn(PlayerController player, int steps)
    {
        if (player.HeldTool?.Kind == HandToolKind.CamKey && WhyNotMount != null)
        {
            if (SpindleTurning?.Invoke() == true)
                PlayerController.Notify(this, "İş mili dönerken kam kilidine dokunulmaz");
            else
                TurnCams(steps);
            return;
        }
        if (player.Held != null || TurnWith?.Invoke(steps) == true)
            return;
        if (Grip.KeyIn)
            TurnKey(steps, player);
        else if (Grip.HasBar && Grip.Torque == 0)
            SlideBar(steps * 2f);
    }
}
