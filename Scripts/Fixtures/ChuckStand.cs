using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>
/// A round stand where a chuck lies face up to have its jaws changed off
/// the machine, as in a real shop. It is carried (F) to any bench, set
/// down and fixed there with a click; its top is a turntable, so the
/// chuck can be turned (wheel, key out) to a comfortable angle. With physical handling on,
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
public partial class ChuckStand : Carryable, IInteractable
{
    private const float M = WorldScale.MmToM;
    /// <summary>A jaw counts as lying in a slot's guides within this many
    /// degrees of square and this far of its body resting on the face.</summary>
    private const float InGuideDeg = 12f, InGuideHeightM = 0.0025f;

    /// <summary>Text about the jaws only without physical handling; with it,
    /// the chuck itself has to be read.</summary>
    private static bool Hints => !WorkshopSettings.PhysicalHandling;

    private AudioStreamPlayer3D _clack, _ratchet, _scrape;

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
    private Node3D _key;
    private bool _keyIn;

    /// <summary>Fixed to the bench where it stands: a chuck can be set on it
    /// (and it is not picked up).</summary>
    public bool Fixed { get; private set; }
    public override bool CanPickUp => !Fixed && Chuck == null;
    /// <summary>How far one wheel notch turns the turntable.</summary>
    private const float TurntableStepDeg = 15f;
    private Node3D _turntable;

    public static ChuckStand Create(float radiusM = 0.14f)
    {
        var stand = new ChuckStand
        {
            Name = "ChuckStand", DisplayName = "Ayna altlığı (döner tablalı)", Mass = 8f,
            CollisionLayer = Fastener.InteractionLayer | 1, CollisionMask = 1,
        };
        BodyParts.Cylinder(stand, "Base", radiusM, 0.02f, new Vector3(0f, 0.01f, 0f), Vector3.Up, BodyParts.DarkSteel);
        // Clamp lugs that fix it to the bench.
        foreach (float side in new[] { -1f, 1f })
            BodyParts.Box(stand, "Lug", new Vector3(0.05f, 0.012f, 0.03f), new Vector3(side * (radiusM + 0.02f), 0.006f, 0f), BodyParts.DarkSteel);
        stand._turntable = new Node3D { Name = "Turntable" };
        stand.AddChild(stand._turntable);
        BodyParts.Cylinder(stand._turntable, "Plate", radiusM * 0.95f, 0.012f, new Vector3(0f, 0.026f, 0f), Vector3.Up, BodyParts.Steel);
        BodyParts.Cylinder(stand._turntable, "Rubber", radiusM * 0.9f, 0.004f, new Vector3(0f, 0.034f, 0f), Vector3.Up,
            BodyParts.Mat(new Color(0.12f, 0.12f, 0.12f), 0f, 0.9f));
        BodyParts.Box(stand._turntable, "Mark", new Vector3(0.02f, 0.002f, 0.006f), new Vector3(radiusM * 0.85f, 0.033f, 0f), BodyParts.Mat(new Color(0.9f, 0.7f, 0.1f)));
        stand.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radiusM, Height = 0.036f }, Position = new Vector3(0f, 0.018f, 0f) });
        stand._watch = new Area3D { Name = "GrooveWatch", Monitoring = false, CollisionLayer = 0, CollisionMask = 1 };
        stand._watch.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.25f, Height = 0.25f }, Position = new Vector3(0f, 0.15f, 0f) });
        stand.AddChild(stand._watch);
        stand._clack = Sound(stand, Audio.SynthSounds.Clack, 0f);
        stand._ratchet = Sound(stand, Audio.SynthSounds.Ratchet, -8f);
        stand._scrape = Sound(stand, Audio.SynthSounds.Scrape, -6f);
        stand._watch.BodyEntered += body => { if (body is JawItem jaw) stand._near.Add(jaw); };
        stand._watch.BodyExited += body =>
        {
            if (body is not JawItem jaw)
                return;
            stand._near.Remove(jaw);
        };
        return stand;
    }

    private ChuckGrip Grip => Chuck?.Grip;
    private ChuckVisual Visual => Chuck?.Visual;
    private static Vector3 SeatOffset => new(0f, 0.036f, 0f);

    /// <summary>Fixes the stand where it lies, stood upright; or lets it go
    /// to be carried elsewhere.</summary>
    private void SetFixed(bool on)
    {
        Fixed = on;
        Freeze = on;
        if (on)
        {
            var yaw = GlobalBasis.GetEuler().Y;
            GlobalBasis = new Basis(Vector3.Up, yaw);
            LinearVelocity = AngularVelocity = Vector3.Zero;
        }
    }

    /// <summary>Starts fixed where it was put (on the cabinet's worktop).</summary>
    public void FixInPlace() => SetFixed(true);

    private void Seat(PlayerController player, ChuckItem chuck)
    {
        player.ReleaseHeld();
        chuck.Reparent(_turntable, false);
        chuck.Transform = new Transform3D(Basis.Identity, SeatOffset);
        chuck.Freeze = true;
        chuck.Seated = true;
        chuck.SetSlotCollision(true);
        Chuck = chuck;
        UpdateWatch();
    }

    private void Lift(PlayerController player)
    {
        var chuck = Chuck;
        Chuck = null;
        chuck.Seated = false;
        chuck.SetSlotCollision(false);
        chuck.Reparent(GetTree().CurrentScene, true);
        player.Give(chuck);
        UpdateWatch();
    }

    /// <summary>The groove watch (which jaw bodies are near the chuck) runs
    /// only while a slot is empty on a jawed chuck with physical handling.
    /// It is only read when the key turns: nothing is worked out per frame.</summary>
    private void UpdateWatch()
    {
        bool open = Chuck != null && !Grip.IsCollet && !Grip.Jaws.Complete && WorkshopSettings.PhysicalHandling;
        _watch.SetDeferred(Area3D.PropertyName.Monitoring, open);
        if (!open)
            _near.Clear();
    }

    public override void _Ready()
    {
        UpdateWatch();
        SetPhysicsProcess(false);
    }

    /// <summary>The jaw body lying in a slot's guides: square to it, upright,
    /// its tongue down in the groove so its body rests on the face. Laid
    /// across, or on its tongue, it sits higher and is not in.</summary>
    private JawItem JawLyingIn(int slot)
    {
        var frame = Visual.GlobalTransform;
        var up = frame.Basis.Y.Normalized();
        var along = Visual.SlotDirection(slot);
        var dir = (frame.Basis * along).Normalized();
        float cos = Mathf.Cos(Mathf.DegToRad(InGuideDeg));
        foreach (var jaw in _near)
        {
            if (!IsInstanceValid(jaw))
                continue;
            var basis = jaw.GlobalBasis.Orthonormalized();
            if (basis.Y.Dot(up) < cos || basis.X.Dot(dir) < cos)
                continue;
            var local = frame.AffineInverse() * jaw.GlobalPosition;
            var flat = new Vector3(local.X, 0f, local.Z);
            float radial = flat.Dot(along), lateral = (flat - along * radial).Length();
            if (Mathf.Abs(local.Y - Visual.FaceM) < InGuideHeightM && lateral < Visual.GrooveWidthM * 0.35f
                && radial > Chuck.Model.BoreMm / 2f * WorldScale.MmToM && radial < Visual.RadiusM)
                return jaw;
        }
        return null;
    }

    /// <summary>After a notch in: the thread start crossing a slot takes the
    /// jaw lying in that slot's guides (an independent chuck's screw takes
    /// any jaw lying in its slot). The one moment a jaw stops being a body.</summary>
    private int? CatchLyingJaws()
    {
        var jaws = Grip.Jaws;
        IEnumerable<int> slots = jaws.Independent ? Enumerable.Range(0, jaws.SlotCount)
            : jaws.Notch % ChuckJaws.NotchesPerSlot == 0 ? new[] { jaws.StartSlot } : System.Array.Empty<int>();
        foreach (int slot in slots)
        {
            if (jaws.Slot(slot) != null || JawLyingIn(slot) is not JawItem body)
                continue;
            // A jaw for another chuck, or from another set, does not engage.
            if (jaws.Place(slot, body.Kind, body.Number, body.MadeFor, body.Family) != null || jaws.Slot(slot) == null)
            {
                jaws.TakeWaiting(slot);
                continue;
            }
            if (GetTree().GetFirstNodeInGroup("player") is PlayerController player && player.Held == body)
                player.ReleaseHeld();
            _near.Remove(body);
            body.QueueFree();
            return slot;
        }
        return null;
    }

    /// <summary>A jaw the scroll let go of: a body again, lying in its
    /// slot's guides where it was, to be slid out by hand.</summary>
    private void Loosen(int slot)
    {
        if (Grip.Jaws.TakeWaiting(slot) is not SeatedJaw jaw)
            return;
        var item = JawItem.Create(jaw.Kind, jaw.Number, Chuck.Model);
        GetTree().CurrentScene.AddChild(item);
        var frame = Visual.GlobalTransform;
        var dir = (frame.Basis * Visual.SlotDirection(slot)).Normalized();
        var up = frame.Basis.Y.Normalized();
        item.GlobalTransform = new Transform3D(new Basis(dir, up, dir.Cross(up)), frame * Visual.GrooveEntry(slot) + up * 0.001f);
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

    public string Prompt(PlayerController player)
    {
        if (!Fixed)
            return player.Held == null ? "Ayna altlığı   [Tık] Buraya sabitle   [F] Al" : null;
        if (Chuck == null)
            return player.Held is ChuckItem held ? $"[Tık] {held.DisplayName} altlığa yüzü yukarı koy"
                : player.Held == null ? "Ayna altlığı: aynayı buraya yüzü yukarı koy   [Tık] Sabitlemeyi çöz (taşımak için)" : null;
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
        if (WaitingSlot() is int looseSlot)
            return Hints ? $"[Tık] {looseSlot + 1}. kanaldaki gevşek çeneyi çek" : "[Tık] Çeneyi kanaldan çek";
        if (_keyIn && !Hints)
            return g.IsCollet ? "Pens aynası: anahtarın burada işi yok" : "[Tekerlek] Anahtarı çevir   [Shift+Tekerlek] Tablayı çevir   [Tık] Anahtarı çıkar";
        if (_keyIn)
            return (g.IsCollet ? "Pens aynası: anahtarın burada işi yok" : g.Jaws.Complete
                ? $"[Tekerlek] Çeneleri aç / kapat   Ø{g.OpeningMm:0} mm" + (g.FullyOpen ? " (tam açık: geri çevirmek çeneleri söker)" : "")
                : $"[Tekerlek] Spirali çevir: başı {g.Jaws.StartSlot + 1}. kanalda")
                + (g.Jaws.Complete && !g.HasBar && g.OpeningMm <= ChuckGrip.JawStepMm && !g.Jaws.MeetAtCentre ? "   ÇENELER ORTADA BULUŞMUYOR" : "")
                + "   [Tık] Anahtarı çıkar";
        if (g.IsCollet && g.Jaws.ColletMm != null)
            return "[Tık] Pensi çıkar";
        return (Hints ? $"{Chuck.Model.DisplayName}: {g.Jaws.Seated}/{g.Jaws.SlotCount} çene   " : $"{Chuck.Model.DisplayName}   ")
            + $"[Tekerlek] Tablayı çevir   [Tık] Aynayı kaldır ({Chuck.Mass:0.#} kg)";
    }

    public void Use(PlayerController player)
    {
        if (!Fixed)
        {
            if (player.Held == null)
                SetFixed(true);
            return;
        }
        if (Chuck == null)
        {
            if (player.Held is ChuckItem chuck)
                Seat(player, chuck);
            else if (player.Held == null)
                SetFixed(false);
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
        else if (WaitingSlot() is int waiting && g.Jaws.TakeWaiting(waiting) is SeatedJaw back)
        {
            var item = JawItem.Create(back.Kind, back.Number, Chuck.Model);
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = GlobalPosition + Vector3.Up * 0.2f;
            player.Give(item);
            Chuck.Refresh();
            UpdateWatch();
        }
        else if (_keyIn)
        {
            SetKey(false);
            var key = HandTool.Create(HandToolKind.ChuckKey);
            GetTree().CurrentScene.AddChild(key);
            key.GlobalPosition = GlobalPosition + Vector3.Up * 0.2f;
            player.Give(key);
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
        if (Chuck == null || player.Held != null)
            return;
        if (!_keyIn || Input.IsKeyPressed(Key.Shift))
        {
            // Key out (or Shift held): the wheel turns the turntable, chuck,
            // key and all.
            _turntable.RotateY(Mathf.DegToRad(TurntableStepDeg) * steps);
            return;
        }
        if (Grip.IsCollet)
            return;
        var g = Grip;
        // The start sitting right at a slot takes the jaw there as it moves on.
        int? leaving = steps > 0 && WorkshopSettings.PhysicalHandling && g.Torque == 0 ? CatchLyingJaws() : null;
        var turned = g.TurnKey(steps);
        if (leaving is int left && turned.Caught == null)
            turned = turned with { Caught = left };
        _key.RotateObjectLocal(Vector3.Back, steps * Mathf.Pi / 6f);
        Play(_ratchet);
        if (WorkshopSettings.PhysicalHandling)
        {
            if (steps > 0 && turned.Caught == null && CatchLyingJaws() is int took)
                turned = turned with { Caught = took };
            if (turned.Released is int let)
                Loosen(let);
        }
        Chuck.Refresh();
        if (turned.Caught is int caught)
        {
            Play(_clack);
            AfterJawChange($"Spiral {caught + 1}. kanaldaki çeneyi yakaladı");
        }
        else if (turned.Released is int released)
        {
            // Let go, it lies loose in its slot: pulled out by hand, or caught
            // again going in.
            Play(_scrape);
            Say($"{released + 1}. kanaldaki çene spiralden kurtuldu, kanalda gevşek duruyor");
            UpdateWatch();
        }
    }

    private void SetKey(bool inside)
    {
        _keyIn = inside;
        Grip.KeyIn = inside;
        if (_key == null)
        {
            _key = new Node3D { Name = "Key" };
            _turntable.AddChild(_key);
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
