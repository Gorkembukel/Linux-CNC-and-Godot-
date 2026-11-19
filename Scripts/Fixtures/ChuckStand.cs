using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// A round stand on a bench where a chuck lies face up to have its jaws
/// changed off the machine, as in a real shop. With physical handling on,
/// a jaw is laid into its groove by hand: brought in square to the groove
/// and flat on the face, it drops into it; the scroll catches it when the
/// key brings the thread start round to that slot. Jaws let go by the
/// scroll slide out of the groove onto the bench. Nothing here is simulated
/// once the chuck is complete: the seated chuck is frozen, fitted jaws are
/// part of its drawing, and the groove watch switches off.
/// With physical handling on it says nothing about how the jaws went in:
/// the operator reads the scroll's ridges down the grooves, feels a jaw
/// being drawn into its groove, and hears it drop and the scroll catch it.
/// </summary>
public partial class ChuckStand : StaticBody3D, IInteractable
{
    private const float M = WorldScale.MmToM;
    /// <summary>A jaw within this many degrees of square to a groove, and
    /// of flat on the face, drops in.</summary>
    private const float AlignDeg = 15f;
    /// <summary>How close above the face a jaw's underside must come (m).</summary>
    private const float DropInHeightM = 0.015f;
    /// <summary>Within this many degrees and a groove's width or so, a
    /// jaw is drawn toward the groove: the hand feels it want to go in.</summary>
    private const float GuideDeg = 35f;
    /// <summary>Spring of that draw (per second squared per metre / per radian).</summary>
    private const float GuidePull = 160f, GuideTurn = 40f;

    /// <summary>Text about the jaws only without physical handling; with it,
    /// the chuck itself has to be read.</summary>
    private static bool Hints => !WorkshopSettings.PhysicalHandling;

    private AudioStreamPlayer3D _clunk, _clack, _ratchet, _scrape;

    private static AudioStreamPlayer3D Sound(Node3D parent, AudioStream stream, float db)
    {
        var player = new AudioStreamPlayer3D { Stream = stream, VolumeDb = db, UnitSize = 1.5f, MaxDistance = 14f };
        parent.AddChild(player);
        return player;
    }

    private static void Play(AudioStreamPlayer3D player)
    {
        player.PitchScale = (float)GD.RandRange(0.93, 1.07);
        player.Play();
    }

    private void Say(string text)
    {
        if (Hints)
            PlayerController.Notify(this, text);
    }

    public ChuckItem Chuck { get; private set; }

    private Area3D _watch;
    private readonly HashSet<JawItem> _near = new();
    /// <summary>Jaws the scroll has just let go of: they lie in their groove
    /// but stay out until picked up (or moved off the chuck).</summary>
    private readonly HashSet<JawItem> _ejected = new();
    private Node3D _key;
    private bool _keyIn;
    private string _lastRefusal;

    public static ChuckStand Create(float radiusM = 0.14f)
    {
        var stand = new ChuckStand { Name = "ChuckStand", CollisionLayer = Fastener.InteractionLayer | 1, CollisionMask = 0 };
        BodyParts.Cylinder(stand, "Pedestal", radiusM, 0.03f, new Vector3(0f, 0.015f, 0f), Vector3.Up, BodyParts.DarkSteel);
        BodyParts.Cylinder(stand, "Rubber", radiusM * 0.9f, 0.004f, new Vector3(0f, 0.032f, 0f), Vector3.Up,
            BodyParts.Mat(new Color(0.12f, 0.12f, 0.12f), 0f, 0.9f));
        stand.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radiusM, Height = 0.034f }, Position = new Vector3(0f, 0.017f, 0f) });
        stand._watch = new Area3D { Name = "GrooveWatch", Monitoring = false, CollisionLayer = 0, CollisionMask = 1 };
        stand._watch.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.25f, Height = 0.25f }, Position = new Vector3(0f, 0.15f, 0f) });
        stand.AddChild(stand._watch);
        stand._clunk = Sound(stand, Audio.SynthSounds.Clunk, -2f);
        stand._clack = Sound(stand, Audio.SynthSounds.Clack, 0f);
        stand._ratchet = Sound(stand, Audio.SynthSounds.Ratchet, -8f);
        stand._scrape = Sound(stand, Audio.SynthSounds.Scrape, -6f);
        stand._watch.BodyEntered += body => { if (body is JawItem jaw) stand._near.Add(jaw); };
        stand._watch.BodyExited += body =>
        {
            if (body is not JawItem jaw)
                return;
            stand._near.Remove(jaw);
            stand._ejected.Remove(jaw);
        };
        return stand;
    }

    private ChuckGrip Grip => Chuck?.Grip;
    private ChuckVisual Visual => Chuck?.Visual;
    private static Vector3 SeatOffset => new(0f, 0.034f, 0f);

    private void Seat(PlayerController player, ChuckItem chuck)
    {
        player.ReleaseHeld();
        chuck.Reparent(this, false);
        chuck.Transform = new Transform3D(Basis.Identity, SeatOffset);
        chuck.Freeze = true;
        chuck.Seated = true;
        Chuck = chuck;
        UpdateWatch();
    }

    private void Lift(PlayerController player)
    {
        var chuck = Chuck;
        Chuck = null;
        chuck.Seated = false;
        chuck.Reparent(GetTree().CurrentScene, true);
        player.Give(chuck);
        UpdateWatch();
    }

    /// <summary>The groove watch runs only while there is a jaw slot to
    /// fill on a scroll or independent chuck, and physical handling is on.</summary>
    private void UpdateWatch()
    {
        bool open = Chuck != null && !Grip.IsCollet && !Grip.Jaws.Complete && WorkshopSettings.PhysicalHandling;
        _watch.SetDeferred(Area3D.PropertyName.Monitoring, open);
        if (!open)
        {
            _near.Clear();
            _ejected.Clear();
        }
        SetPhysicsProcess(open);
    }

    public override void _Ready() => UpdateWatch();

    public override void _PhysicsProcess(double delta)
    {
        foreach (var jaw in _near.ToArray())
        {
            if (!IsInstanceValid(jaw))
            {
                _near.Remove(jaw);
                _ejected.Remove(jaw);
                continue;
            }
            if (_ejected.Contains(jaw))
            {
                if (GetTree().GetFirstNodeInGroup("player") is not PlayerController holder || holder.Held != jaw)
                    continue;
                _ejected.Remove(jaw);
            }
            if (GrooveFor(jaw) is int slot)
                DropIn(jaw, slot);
            else
                Guide(jaw);
        }
    }

    /// <summary>A jaw nearly square over a groove is drawn into line with it
    /// and down onto the face, a little: in the hand that feels like the
    /// groove taking it.</summary>
    private void Guide(JawItem jaw)
    {
        var frame = Visual.GlobalTransform;
        var up = frame.Basis.Y.Normalized();
        var jawBasis = jaw.GlobalBasis.Orthonormalized();
        var local = frame.AffineInverse() * jaw.GlobalPosition;
        if (local.Y > Visual.FaceM + 0.04f || jawBasis.Y.Dot(up) < Mathf.Cos(Mathf.DegToRad(GuideDeg)))
            return;
        var flat = new Vector3(local.X, 0f, local.Z);
        for (int slot = 0; slot < Visual.Slots; slot++)
        {
            if (Grip.Jaws.Slot(slot) != null || Grip.Jaws.Waiting(slot) != null)
                continue;
            var along = Visual.SlotDirection(slot);
            var dir = (frame.Basis * along).Normalized();
            if (jawBasis.X.Dot(dir) < Mathf.Cos(Mathf.DegToRad(GuideDeg)))
                continue;
            float radial = flat.Dot(along);
            var off = flat - along * radial;
            if (off.Length() > Visual.RadiusM * 0.3f || radial < Visual.RadiusM * 0.2f || radial > Visual.RadiusM * 1.1f)
                continue;
            float m = jaw.Mass;
            var sideways = frame.Basis * off;
            float height = local.Y - Visual.FaceM;
            jaw.ApplyCentralForce(m * (-sideways * GuidePull - up * Mathf.Max(0f, height) * GuidePull * 0.5f - jaw.LinearVelocity * 4f));
            var square = jawBasis.X.Cross(dir) + jawBasis.Y.Cross(up);
            jaw.ApplyTorque(m * 0.002f * (square * GuideTurn - jaw.AngularVelocity * 2f));
            return;
        }
    }

    /// <summary>The groove a jaw lies square in, flat on the face, near its
    /// outer end; null when it does not.</summary>
    private int? GrooveFor(JawItem jaw)
    {
        var frame = Visual.GlobalTransform;
        var up = frame.Basis.Y.Normalized();
        var jawBasis = jaw.GlobalBasis.Orthonormalized();
        if (jawBasis.Y.Dot(up) < Mathf.Cos(Mathf.DegToRad(AlignDeg)))
            return null;
        // The jaw's own frame: X along it from the gripping face outward.
        var local = frame.AffineInverse() * jaw.GlobalPosition;
        if (local.Y < Visual.FaceM - 0.004f || local.Y > Visual.FaceM + DropInHeightM)
            return null;
        float cos = Mathf.Cos(Mathf.DegToRad(AlignDeg));
        for (int slot = 0; slot < Visual.Slots; slot++)
        {
            var dir = frame.Basis * Visual.SlotDirection(slot);
            dir = dir.Normalized();
            if (jawBasis.X.Dot(dir) < cos)
                continue;
            var along = Visual.SlotDirection(slot);
            var flat = new Vector3(local.X, 0f, local.Z);
            float radial = flat.Dot(along), lateral = (flat - along * radial).Length();
            if (lateral < Visual.RadiusM * 0.1f && radial > Visual.RadiusM * 0.3f && radial < Visual.RadiusM)
                return slot;
        }
        return null;
    }

    private void DropIn(JawItem jaw, int slot)
    {
        if (Grip.Jaws.WhyNotJaw(slot, jaw.Kind, jaw.Number, jaw.MadeFor, jaw.Family) is string why)
        {
            // It just will not go in: the tongue does not fit, or the
            // groove is taken. Said in words only without physical handling.
            if (why != _lastRefusal)
                Say(why);
            _lastRefusal = why;
            return;
        }
        _lastRefusal = null;
        if (GetTree().GetFirstNodeInGroup("player") is PlayerController player && player.Held == jaw)
            player.ReleaseHeld();
        int seatedBefore = Grip.Jaws.Seated;
        Grip.Jaws.Place(slot, jaw.Kind, jaw.Number, jaw.MadeFor, jaw.Family);
        _near.Remove(jaw);
        jaw.QueueFree();
        Play(_clunk);
        if (Grip.Jaws.Seated > seatedBefore)
            Play(_clack);
        AfterJawChange($"{jaw.DisplayName} {slot + 1}. kanala oturdu");
    }

    private void AfterJawChange(string what)
    {
        Chuck.Refresh();
        UpdateWatch();
        if (!Hints)
            return;
        var jaws = Grip.Jaws;
        string next = jaws.Complete ? "Ayna tamam: anahtarla kapatıp çenelerin ortada buluştuğuna bak"
            : jaws.Independent ? "Sıradaki çeneyi kanalına yatır"
            : Enumerable.Range(0, jaws.SlotCount).Any(i => jaws.Waiting(i) != null)
                ? "Çene spirale dayalı bekliyor: anahtarı çevir, spiralin başı kanala gelince yakalar"
                : "Sıradaki numaralı çeneyi kanalına yatır";
        PlayerController.Notify(this, $"{what}. {next}");
    }

    /// <summary>A jaw the scroll let go of slides out of its groove onto the bench.</summary>
    private void Eject(SeatedJaw jaw, int slot)
    {
        var item = JawItem.Create(jaw.Kind, jaw.Number, Chuck.Model);
        GetTree().CurrentScene.AddChild(item);
        var frame = Visual.GlobalTransform;
        var dir = (frame.Basis * Visual.SlotDirection(slot)).Normalized();
        var up = frame.Basis.Y.Normalized();
        item.GlobalTransform = new Transform3D(new Basis(dir, up, dir.Cross(up)), frame * Visual.GrooveEntry(slot) + up * 0.004f);
        item.LinearVelocity = dir * 0.35f;
        _ejected.Add(item);
        Play(_scrape);
        Say($"{jaw.Number} numaralı çene spiralden kurtulup kanaldan çıktı");
    }

    public string Prompt(PlayerController player)
    {
        if (Chuck == null)
            return player.Held is ChuckItem held ? $"[Tık] {held.DisplayName} altlığa yüzü yukarı koy" : "Ayna altlığı: aynayı buraya yüzü yukarı koy, çenelerini burada değiştir";
        var g = Grip;
        if (player.HeldTool?.Kind == HandToolKind.ChuckKey)
            return _keyIn ? null : "[Tık] Anahtarı aynaya tak";
        if (player.Held is JawItem jaw)
        {
            if (!Hints)
                return null;
            if (!jaw.Fits(Chuck.Model))
                return "Bu çene bu aynanın değil";
            return NearestSlot(player) is int slot ? $"[Tık] {jaw.DisplayName} → {slot + 1}. kanala yatır" : null;
        }
        if (player.Held is ColletItem collet)
            return g.IsCollet ? $"[Tık] {collet.DisplayName} tak" : "Bu ayna pens almaz";
        if (player.Held != null)
            return null;
        if (_keyIn && !Hints)
            return g.IsCollet ? "Pens aynası: anahtarın burada işi yok" : "[Tekerlek] Anahtarı çevir   [Tık] Anahtarı çıkar";
        if (_keyIn)
            return (g.IsCollet ? "Pens aynası: anahtarın burada işi yok" : g.Jaws.Complete
                ? $"[Tekerlek] Çeneleri aç / kapat   Ø{g.OpeningMm:0} mm" + (g.FullyOpen ? " (tam açık: geri çevirmek çeneleri söker)" : "")
                : $"[Tekerlek] Spirali çevir: başı {g.Jaws.StartSlot + 1}. kanalda")
                + (g.Jaws.Complete && !g.HasBar && g.OpeningMm <= ChuckGrip.JawStepMm && !g.Jaws.MeetAtCentre ? "   ÇENELER ORTADA BULUŞMUYOR" : "")
                + "   [Tık] Anahtarı çıkar";
        if (WaitingSlot() is int waiting)
            return Hints ? $"[Tık] {waiting + 1}. kanalda bekleyen çeneyi geri al" : "[Tık] Bekleyen çeneyi kanaldan geri al";
        if (g.IsCollet && g.Jaws.ColletMm != null)
            return "[Tık] Pensi çıkar";
        return (Hints ? $"{Chuck.Model.DisplayName}: {g.Jaws.Seated}/{g.Jaws.SlotCount} çene   " : $"{Chuck.Model.DisplayName}   ")
            + $"[Tık] Aynayı kaldır ({Chuck.Mass:0.#} kg)";
    }

    public void Use(PlayerController player)
    {
        if (Chuck == null)
        {
            if (player.Held is ChuckItem chuck)
                Seat(player, chuck);
            return;
        }
        var g = Grip;
        if (player.HeldTool?.Kind == HandToolKind.ChuckKey && !_keyIn)
        {
            player.ReleaseHeld().QueueFree();
            SetKey(true);
        }
        else if (player.Held is JawItem jaw && !WorkshopSettings.PhysicalHandling && NearestSlot(player) is int slot)
        {
            if (g.Jaws.Place(slot, jaw.Kind, jaw.Number, jaw.MadeFor, jaw.Family) is string why)
                PlayerController.Notify(this, why);
            else
            {
                player.ReleaseHeld().QueueFree();
                AfterJawChange($"{jaw.DisplayName} {slot + 1}. kanala oturdu");
            }
        }
        else if (player.Held is ColletItem collet && g.InstallCollet(collet.SizeMm) == null)
        {
            player.ReleaseHeld().QueueFree();
            Chuck.Refresh();
        }
        else if (player.Held != null)
            return;
        else if (_keyIn)
        {
            SetKey(false);
            var key = HandTool.Create(HandToolKind.ChuckKey);
            GetTree().CurrentScene.AddChild(key);
            key.GlobalPosition = GlobalPosition + Vector3.Up * 0.2f;
            player.Give(key);
        }
        else if (WaitingSlot() is int waiting && g.Jaws.TakeWaiting(waiting) is SeatedJaw back)
        {
            var item = JawItem.Create(back.Kind, back.Number, Chuck.Model);
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = GlobalPosition + Vector3.Up * 0.2f;
            player.Give(item);
            Chuck.Refresh();
            UpdateWatch();
        }
        else if (g.IsCollet && g.TakeCollet() is float size)
        {
            var item = ColletItem.Create(size);
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = GlobalPosition + Vector3.Up * 0.2f;
            player.Give(item);
            Chuck.Refresh();
        }
        else
            Lift(player);
    }

    public void Turn(PlayerController player, int steps)
    {
        if (Chuck == null || !_keyIn || player.Held != null || Grip.IsCollet)
            return;
        var g = Grip;
        int before = g.Jaws.Seated;
        var released = g.TurnKey(steps, canTakeJaw: true);
        Play(_ratchet);
        if (released is SeatedJaw jaw)
            Eject(jaw, g.Jaws.LastReleasedSlot);
        Chuck.Refresh();
        if (g.Jaws.Seated > before)
        {
            Play(_clack);
            AfterJawChange($"Spiral {g.Jaws.StartSlot + 1}. kanaldaki çeneyi yakaladı");
        }
        else if (released != null)
            UpdateWatch();
    }

    private void SetKey(bool inside)
    {
        _keyIn = inside;
        Grip.KeyIn = inside;
        if (_key == null)
        {
            _key = new Node3D { Name = "Key" };
            AddChild(_key);
            BodyParts.Cylinder(_key, "Shaft", 0.007f, 0.1f, new Vector3(0f, 0f, 0.05f), Vector3.Back, BodyParts.Steel);
            BodyParts.Cylinder(_key, "Handle", 0.006f, 0.14f, new Vector3(0f, 0f, 0.1f), Vector3.Right, BodyParts.Steel);
        }
        _key.Visible = inside;
        if (Chuck != null)
            _key.Position = SeatOffset + new Vector3(0f, Visual.FaceM * 0.6f, Visual.RadiusM);
    }

    private int? WaitingSlot()
    {
        for (int i = 0; i < Grip.Jaws.SlotCount; i++)
            if (Grip.Jaws.Waiting(i) != null)
                return i;
        return null;
    }

    /// <summary>The empty groove nearest where the player looks.</summary>
    private int? NearestSlot(PlayerController player)
    {
        var local = Visual.GlobalTransform.AffineInverse() * player.LookPoint;
        var flat = new Vector3(local.X, 0f, local.Z);
        int? best = null;
        float bestDot = -2f;
        for (int i = 0; i < Grip.Jaws.SlotCount; i++)
        {
            if (Grip.Jaws.Slot(i) != null || Grip.Jaws.Waiting(i) != null)
                continue;
            float dot = flat.Normalized().Dot(Visual.SlotDirection(i));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }
        return best;
    }
}
