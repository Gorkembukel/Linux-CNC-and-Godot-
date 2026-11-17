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
/// A jaw chuck the player works with the chuck key: key in, wheel to open,
/// close and tighten the jaws, key OUT again. Holds one round bar. Local
/// frame (metres): X along the spindle axis pointing out of the chuck, origin
/// at the front face of the jaws on the centreline.
/// </summary>
[Tool]
public partial class JawChuck : StaticBody3D, IInteractable, IStockFixture, IMeasurable
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

    private Node3D _rotor, _key;
    private readonly List<MeshInstance3D> _jaws = new();
    private CollisionShape3D _barShape;

    public static JawChuck Create(float diameterMm, int jawCount, float jawDepthMm, float boreDepthMm)
    {
        var chuck = new JawChuck
        {
            Name = "Chuck",
            Grip = new ChuckGrip(diameterMm * 0.8f, jawDepthMm, diameterMm * 0.3f, boreDepthMm),
            RadiusM = diameterMm / 2f * M,
            CollisionLayer = Fastener.InteractionLayer,
            CollisionMask = 0,
        };
        float depth = jawDepthMm * M, radius = chuck.RadiusM;
        float bodyLength = 0.05f + depth * 0.3f, bodyCenter = -depth * 0.7f - bodyLength / 2f;

        chuck._rotor = new Node3D { Name = "Rotor" };
        chuck.AddChild(chuck._rotor);
        BodyParts.Cylinder(chuck._rotor, "Body", radius, bodyLength, new Vector3(bodyCenter, 0f, 0f), Vector3.Right, BodyParts.Steel);
        for (int i = 0; i < jawCount; i++)
        {
            var arm = new Node3D { Name = "Jaw", RotationDegrees = new Vector3(i * 360f / jawCount, 0f, 0f) };
            chuck._rotor.AddChild(arm);
            chuck._jaws.Add(BodyParts.Box(arm, "JawBlock", new Vector3(depth * 0.7f, radius * 0.45f, JawHeight), Vector3.Zero, BodyParts.DarkSteel));
        }

        chuck._key = new Node3D { Name = "Key", Position = new Vector3(bodyCenter, radius, 0f), Visible = false };
        chuck._rotor.AddChild(chuck._key);
        BodyParts.Cylinder(chuck._key, "Shaft", 0.007f, 0.12f, new Vector3(0f, 0.05f, 0f), Vector3.Up, BodyParts.Steel);
        BodyParts.Cylinder(chuck._key, "Handle", 0.006f, 0.14f, new Vector3(0f, 0.11f, 0f), Vector3.Right, BodyParts.Steel);

        chuck.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = bodyLength + depth * 0.7f },
            Position = new Vector3(-(bodyLength + depth * 0.7f) / 2f, 0f, 0f),
            RotationDegrees = new Vector3(0f, 0f, 90f),
        });
        chuck._barShape = new CollisionShape3D { Shape = new CylinderShape3D(), RotationDegrees = new Vector3(0f, 0f, 90f), Disabled = true };
        chuck.AddChild(chuck._barShape);
        chuck.Refresh();
        return chuck;
    }

    private void Refresh()
    {
        float inner = Grip.OpeningMm / 2f * M, jawRadial = RadiusM * 0.45f;
        foreach (var jaw in _jaws)
            jaw.Position = new Vector3(-Grip.JawDepthMm * M * 0.35f, inner + jawRadial / 2f, 0f);
        _key.Visible = Grip.KeyIn;

        _barShape.Disabled = !Grip.HasBar;
        if (!Grip.HasBar)
            return;
        float stickOut = Mathf.Max(0.002f, Grip.StickOutMm * M);
        var shape = (CylinderShape3D)_barShape.Shape;
        shape.Radius = Grip.BarDiameterMm / 2f * M;
        shape.Height = stickOut;
        _barShape.Position = new Vector3(stickOut / 2f, 0f, 0f);
        if (Stock != null)
            Stock.Transform = new Transform3D(Basis.Identity, new Vector3((Grip.StickOutMm - Stock.Blank.LengthMm / 2f) * M, 0f, 0f));
    }

    /// <summary>Turns the jaws and the key with the spindle.</summary>
    public void Spin(float degrees) => _rotor.RotateX(Mathf.DegToRad(degrees));

    /// <summary>The raw bar's own look, hidden while something else draws the part.</summary>
    public void ShowRawStock(bool visible) => Seat.ShowRaw(visible);

    public RigidBody3D TakeStock() => TakeBar();

    public string ToolCrash(Vector3 tip, float radius) =>
        tip.X < 0f && new Vector2(tip.Y, tip.Z).Length() < RadiusM + radius ? "ÇARPMA: takım aynaya girdi" : null;

    public string SheetLines
    {
        get
        {
            var g = Grip;
            string text = Sheet.Mark(g.HasBar) + "Kütük aynada"
                + (g.HasBar ? System.FormattableString.Invariant($"  Ø{g.BarDiameterMm:0.#} x {g.BarLengthMm:0.#}") : "") + "\n";
            if (!g.HasBar)
                return text + "    Ayna anahtarını tak, çeneleri aç,\n    yuvarlak çubuğu aynaya sür\n";
            text += Sheet.Mark(g.GripLengthMm >= ChuckGrip.MinGripMm)
                + System.FormattableString.Invariant($"Çıkıntı {g.StickOutMm:0} mm, çenede {g.GripLengthMm:0} mm\n");
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

    public void TurnKey(int steps)
    {
        Grip.TurnKey(steps);
        Refresh();
    }

    public bool InsertBar(StockItem item)
    {
        if (item.Blank.Shape != StockShape.CylinderAlongX || !Grip.Insert(item.GripDiameterMm, item.Blank.LengthMm))
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
        var tool = player.HeldTool?.Kind;
        var g = Grip;
        if (tool == HandToolKind.ChuckKey)
            return "[Tık] Anahtarı aynaya tak";
        if (player.Held is StockItem held)
        {
            if (g.HasBar)
                return "Aynada zaten bir kütük var";
            if (held.Blank.Shape != StockShape.CylinderAlongX)
                return "Ayna yalnızca yuvarlak çubuk tutar";
            if (MilledBefore(held))
                return "Frezede işlenmiş parça aynaya takılmaz";
            if (AxisAlong(player) is not bool reversed)
                return "Parçayı ayna ekseni doğrultusunda tut ([Z/X] ile çevir)";
            float diameter = GripDiameter(player, reversed);
            if (diameter > g.MaxOpeningMm)
                return $"Parça aynaya sığmaz (en fazla Ø{g.MaxOpeningMm:0} mm)";
            string end = held.Turned != null ? (reversed ? "  (+X ucu aynaya)" : "  (-X ucu aynaya)") : "";
            return g.CanInsert(diameter)
                ? "[Tık] Aynaya sür" + end
                : $"Çeneler dar: Ø{g.OpeningMm:0} mm, tutulan uç Ø{diameter:0.#} mm (ayna anahtarıyla aç)";
        }
        if (player.Held != null)
            return null;
        if (g.KeyIn)
            return (g.HasBar && g.OpeningMm <= g.BarDiameterMm
                ? $"[Tekerlek] Aynayı sık / gevşet   sıkma {g.Torque}/{ChuckGrip.FullTorque}"
                : $"[Tekerlek] Çeneleri aç / kapat   Ø{g.OpeningMm:0} mm") + "   [Tık] Anahtarı çıkar";
        if (g.HasBar)
            return g.Torque > 0
                ? $"Ayna sıkılı ({g.Torque}/{ChuckGrip.FullTorque}), çıkıntı {g.StickOutMm:0} mm. Gevşetmek için ayna anahtarını tak"
                : $"[Tekerlek] Kütüğü it / çek   çıkıntı {g.StickOutMm:0} mm   [Tık] Kütüğü al";
        return $"Ayna   çene açıklığı Ø{g.OpeningMm:0} mm (ayna anahtarı gerekli)";
    }

    public void Use(PlayerController player)
    {
        if (player.HeldTool?.Kind == HandToolKind.ChuckKey)
        {
            player.ReleaseHeld().QueueFree();
            InsertKey();
        }
        else if (player.Held is StockItem { Blank.Shape: StockShape.CylinderAlongX } item && !MilledBefore(item) && !Grip.HasBar
                 && AxisAlong(player) is bool reversed && Grip.CanInsert(GripDiameter(player, reversed)))
        {
            player.ReleaseHeld();
            if (reversed)
                item.ReverseTurned();
            InsertBar(item);
        }
        else if (player.Held == null && Grip.KeyIn)
            player.Give(TakeKey());
        else if (player.Held == null && Grip.HasBar && Grip.Torque == 0)
            player.Give(TakeBar());
    }

    /// <summary>Whether the carried part lies along the spindle: false = as
    /// it is, true = end for end; null = across the axis.</summary>
    private bool? AxisAlong(PlayerController player)
    {
        float along = (player.HeldTurnIn(this) * Vector3.Right).X;
        return Mathf.Abs(along) < 0.5f ? null : along < 0f;
    }

    private static float GripDiameter(PlayerController player, bool reversed) =>
        player.Held is not StockItem item ? 0f
        : item.Turned is { } t ? 2f * (reversed ? t.RadiiMm[^1] : t.RadiiMm[0])
        : item.Blank.DiameterMm;

    private static bool MilledBefore(StockItem item) => item.GetChildren().OfType<VoxelStockVisual>().Any();

    public void Turn(PlayerController player, int steps)
    {
        if (player.Held != null)
            return;
        if (Grip.KeyIn)
            TurnKey(steps);
        else if (Grip.HasBar && Grip.Torque == 0)
            SlideBar(steps * 2f);
    }
}
