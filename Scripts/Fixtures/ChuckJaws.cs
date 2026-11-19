using System;
using System.Collections.Generic;
using System.Linq;

namespace CncTrainer.Fixtures;

/// <summary>A chuck's own sizes, whatever lathe it is on (mm). Engine-free;
/// <see cref="ChuckModel"/> is the editable resource behind it.</summary>
/// <param name="ScrollPitchMm">Scroll chucks: how far a jaw moves per turn
/// of the scroll, so how far off a jaw engaged a turn late sits.</param>
/// <param name="JawFamily">Which jaws fit (they are made for one chuck
/// kind and size); empty = any jaws of the right kind.</param>
public readonly record struct ChuckDims(ChuckKind Kind, int JawCount, float MaxOpeningMm, float JawDepthMm,
    float BoreMm, float BoreDepthMm, float ScrollPitchMm = 6f, string JawFamily = "");

/// <summary>A jaw as it sits in a slot: which set and number it is, and on
/// which turn of the scroll it caught.</summary>
/// <param name="CaughtNotch">Where the key was when the scroll caught it:
/// the jaw has come in with the scroll since.</param>
public readonly record struct SeatedJaw(JawKind Kind, int Number, int Turn, int CaughtNotch = 0);

/// <summary>What one notch of the scroll did: the slot whose jaw it
/// caught, or let go of (null: neither).</summary>
public readonly record struct ScrollEvent(int? Caught, int? Released);

/// <summary>
/// What a chuck grips with, and how it gets there. A scroll chuck's single
/// spiral thread moves all its jaws together; everything follows from where
/// the key has the scroll (<see cref="Notch"/>) and where each jaw was caught:
/// <list type="bullet">
/// <item>A jaw lies loose in its slot until the thread's start, turning in,
/// crosses that slot: then it is caught and comes in with the scroll.</item>
/// <item>Its gripping radius is the scroll's position less where it was
/// caught, less its number's tooth offset (jaw n's first tooth is cut
/// (n-1)/N of a pitch deeper). Jaws 1..N each caught in its own slot on
/// the same turn come out level: they meet at the centre.</item>
/// <item>Faults: a jaw in another number's slot sits off by that share of a
/// pitch; one laid in after the start has passed its slot is caught a turn
/// later and sits a whole pitch out.</item>
/// <item>Turned back past the point it was caught, a jaw is let go: it lies
/// loose in its slot again (taken out by hand, or caught again going in -
/// a turn late if the start has gone round). A notch too far and back does
/// nothing to a jaw already caught.</item>
/// </list>
/// An independent chuck's jaws each have their own screw: any slot, any
/// order, set by hand. A collet chuck takes one collet, made for one size.
/// Engine-free.
/// </summary>
public sealed class ChuckJaws
{
    /// <summary>Key notches that move the scroll's thread start from one
    /// slot to the next.</summary>
    public const int NotchesPerSlot = 3;
    /// <summary>A collet holds bars from its size down to this much under it.</summary>
    public const float ColletRangeMm = 1f;
    /// <summary>Jaws whose centre is off by less than this meet at the centre.</summary>
    public const float CentredMm = 0.02f;

    public readonly ChuckDims Dims;
    private readonly SeatedJaw?[] _slots;
    /// <summary>Jaws lying loose in a slot: not (or no longer) held by the
    /// thread, taken out by hand or caught when the start crosses the slot.</summary>
    private readonly SeatedJaw?[] _loose;
    public int Notch { get; private set; }
    /// <summary>The slot the last jaw let go of came out of.</summary>
    public int LastReleasedSlot { get; private set; } = -1;
    /// <summary>The collet in a collet chuck (its size), null when empty.</summary>
    public float? ColletMm { get; private set; }

    public bool IsCollet => Dims.Kind == ChuckKind.Collet;
    public bool Independent => Dims.Kind == ChuckKind.FourJawIndependent;
    public bool Scroll => !IsCollet && !Independent;
    public int SlotCount => _slots.Length;
    public SeatedJaw? Slot(int index) => _slots[index];
    public int Seated => _slots.Count(s => s != null);
    public bool Complete => !IsCollet && Seated == _slots.Length;
    public bool Empty => IsCollet ? ColletMm == null : Seated == 0 && _loose.All(l => l == null);
    /// <summary>Ready to hold a part: all jaws in, or a collet in.</summary>
    public bool Ready => IsCollet ? ColletMm != null : Complete;
    /// <summary>The set the jaws belong to (they cannot mix).</summary>
    public JawKind? Set => _slots.Concat(_loose).FirstOrDefault(s => s != null)?.Kind;
    private int TurnNotches => Math.Max(1, _slots.Length) * NotchesPerSlot;
    /// <summary>Turns of the scroll from where it started (fraction included).</summary>
    public float ScrollTurns => Notch / (float)TurnNotches;
    /// <summary>The slot the scroll's thread start is at or has just passed (0-based).</summary>
    public int StartSlot => FloorMod(Notch, TurnNotches) / NotchesPerSlot;

    /// <summary>Gripping radius of a jaw just caught by the start of the
    /// thread (mm): with jaws in their own slots, all of them caught in
    /// order sit at half the chuck's widest opening.</summary>
    private float CatchRadiusMm => Dims.MaxOpeningMm / 2f + Dims.ScrollPitchMm * (_slots.Length - 1) / _slots.Length;

    /// <param name="fitted">The set already in, correctly, the jaws three
    /// quarters open; null = no jaws (or no collet).</param>
    public ChuckJaws(ChuckDims dims, JawKind? fitted = JawKind.Hard)
    {
        Dims = dims;
        _slots = new SeatedJaw?[dims.Kind == ChuckKind.Collet ? 0 : Math.Max(2, dims.JawCount)];
        _loose = new SeatedJaw?[_slots.Length];
        if (fitted is not JawKind kind)
            return;
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = new SeatedJaw(kind, i + 1, 0, i * NotchesPerSlot);
        if (Scroll)
            Notch = (int)Math.Round((CatchRadiusMm - 0.375f * dims.MaxOpeningMm) * TurnNotches / dims.ScrollPitchMm);
    }

    /// <summary>A scroll jaw's gripping radius now (mm); NaN for an empty slot.</summary>
    public float RadiusMm(int slot)
    {
        if (_slots[slot] is not SeatedJaw jaw)
            return float.NaN;
        float pitch = Dims.ScrollPitchMm;
        return CatchRadiusMm - pitch * (Notch - jaw.CaughtNotch) / TurnNotches - pitch * (jaw.Number - 1) / _slots.Length;
    }

    /// <summary>The radius the innermost jaw is at (mm): what a bar meets first.</summary>
    public float MinRadiusMm => Enumerable.Range(0, _slots.Length).Where(i => _slots[i] != null).Select(RadiusMm).DefaultIfEmpty(float.NaN).Min();

    /// <summary>The radius the jaws would reach one notch further in.</summary>
    public float MinRadiusAfterMm(int notches) => MinRadiusMm - Dims.ScrollPitchMm * notches / TurnNotches;

    /// <summary>Turns the scroll. Going in, a loose jaw whose slot the thread
    /// start crosses is caught; going out, a jaw turned back past where it
    /// was caught is let go and lies loose in its slot.</summary>
    public ScrollEvent Advance(int notches)
    {
        int? caught = null, released = null;
        for (int n = 0; n < Math.Abs(notches); n++)
        {
            if (notches > 0)
            {
                Notch++;
                if (!Scroll || _slots.Length == 0 || FloorMod(Notch, NotchesPerSlot) != 0)
                    continue;
                int slot = StartSlot;
                if (_loose[slot] is SeatedJaw jaw && _slots[slot] == null)
                {
                    _loose[slot] = null;
                    _slots[slot] = Caught(jaw);
                    caught = slot;
                }
            }
            else
            {
                Notch--;
                if (!Scroll)
                    continue;
                for (int slot = 0; slot < _slots.Length; slot++)
                    if (_slots[slot] is SeatedJaw jaw && jaw.CaughtNotch > Notch)
                    {
                        _slots[slot] = null;
                        _loose[slot] = jaw;
                        LastReleasedSlot = slot;
                        released = slot;
                    }
            }
        }
        return new ScrollEvent(caught, released);
    }

    private SeatedJaw Caught(SeatedJaw jaw) => jaw with { Turn = FloorDiv(Notch, TurnNotches), CaughtNotch = Notch };

    /// <summary>The jaw lying loose in a slot, if any.</summary>
    public SeatedJaw? Waiting(int slot) => _loose[slot];

    /// <summary>Why a jaw cannot go into this slot (the wrong chuck, another
    /// set, the number already in, the slot taken), or null.</summary>
    public string WhyNotJaw(int slot, JawKind kind, int number, ChuckKind madeFor, string family)
    {
        if (IsCollet)
            return "Pens aynasına çene takılmaz";
        if (madeFor != Dims.Kind || (!string.IsNullOrEmpty(family) && !string.IsNullOrEmpty(Dims.JawFamily) && family != Dims.JawFamily))
            return "Bu çene bu aynanın değil";
        if (Set is JawKind set && set != kind)
            return "Farklı takım çeneler karıştırılmaz: önce takılı olanları sök";
        if (_slots.Concat(_loose).Any(s => s?.Number == number))
            return $"{number} numaralı çene zaten takılı";
        if (slot < 0 || slot >= _slots.Length || _slots[slot] != null || _loose[slot] != null)
            return "Bu kanal dolu";
        return null;
    }

    /// <summary>Lays a jaw into a slot's groove: an independent chuck's jaw is
    /// in at once (its own screw), a scroll chuck's lies loose until the
    /// thread start crosses the slot - unless the start is right there.
    /// Returns why not, or null.</summary>
    public string Place(int slot, JawKind kind, int number, ChuckKind madeFor, string family)
    {
        if (WhyNotJaw(slot, kind, number, madeFor, family) is string why)
            return why;
        var jaw = new SeatedJaw(kind, number, 0, Notch);
        if (Independent)
            _slots[slot] = jaw;
        else if (FloorMod(Notch, TurnNotches) == slot * NotchesPerSlot)
            _slots[slot] = Caught(jaw);
        else
            _loose[slot] = jaw;
        return null;
    }

    /// <summary>Lifts a loose jaw out of its groove.</summary>
    public SeatedJaw? TakeWaiting(int slot)
    {
        var jaw = _loose[slot];
        _loose[slot] = null;
        return jaw;
    }

    /// <summary>The first slot with a loose jaw in it, or null.</summary>
    public int? LooseSlot => Array.FindIndex(_loose, l => l != null) is var i && i >= 0 ? i : null;

    /// <summary>The slot the next jaw goes into the easy way: on a scroll
    /// chuck the one the thread start is at, on an independent one the
    /// first empty one; null when that slot is taken.</summary>
    public int? OpenSlot => Independent
        ? Array.FindIndex(_slots, s => s == null) is var i && i >= 0 ? i : null
        : _slots.Length > 0 && _slots[StartSlot] == null && _loose[StartSlot] == null ? StartSlot : null;

    /// <summary>Puts a jaw into the slot the start is at, caught as it
    /// should be (the guided way, with hints). Returns why not, or null.</summary>
    public string Install(JawKind kind, int number, ChuckKind madeFor, string family = "")
    {
        if (!IsCollet && OpenSlot is not int)
            return Independent ? "Bütün yuvalar dolu" : $"Spiralin başı {StartSlot + 1}. yuvada ve orası dolu: anahtarı çevir";
        int slot = OpenSlot ?? -1;
        if (WhyNotJaw(slot, kind, number, madeFor, family) is string why)
            return why;
        // Caught at the crossing of this slot, as if laid in just before it.
        int crossing = Notch - FloorMod(Notch, NotchesPerSlot);
        _slots[slot] = new SeatedJaw(kind, number, FloorDiv(crossing, TurnNotches), Independent ? 0 : crossing);
        return null;
    }

    /// <summary>Independent chuck, opened past the end: the last jaw's screw
    /// lets go of it and it lies loose. Returns its slot, or null.</summary>
    public int? ReleaseLast()
    {
        int slot = Array.FindLastIndex(_slots, s => s != null);
        if (slot < 0)
            return null;
        _loose[slot] = _slots[slot];
        _slots[slot] = null;
        LastReleasedSlot = slot;
        return slot;
    }

    /// <summary>The latest point a caught jaw was caught at: turning back past
    /// it starts letting jaws go.</summary>
    public int LastCatchNotch => _slots.Where(s => s != null).Select(s => s.Value.CaughtNotch).DefaultIfEmpty(int.MinValue).Max();

    public string InstallCollet(float sizeMm)
    {
        if (!IsCollet)
            return "Bu ayna pens almaz";
        if (ColletMm != null)
            return "Aynada zaten bir pens var";
        ColletMm = sizeMm;
        return null;
    }

    public float? TakeCollet()
    {
        var collet = ColletMm;
        ColletMm = null;
        return collet;
    }

    /// <summary>How far out each slot's jaw sits against the others (mm).
    /// Zero for independent jaws (each set by hand).</summary>
    public float ErrorMm(int slot)
    {
        if (!Scroll || _slots[slot] == null || Seated == 0)
            return 0f;
        float mean = Enumerable.Range(0, _slots.Length).Where(i => _slots[i] != null).Average(RadiusMm);
        return RadiusMm(slot) - mean;
    }

    /// <summary>Where the jaws' centre sits off the spindle's because of
    /// how they went in (mm): a jaw further out lets the part over to it.</summary>
    public (float X, float Y) CentreErrorMm()
    {
        if (!Complete || !Scroll)
            return (0f, 0f);
        int n = _slots.Length;
        float x = 0f, y = 0f;
        for (int i = 0; i < n; i++)
        {
            double a = Math.Tau * i / n;
            float e = ErrorMm(i);
            x += e * (float)Math.Cos(a);
            y += e * (float)Math.Sin(a);
        }
        return (2f / n * x, 2f / n * y);
    }

    public bool MeetAtCentre
    {
        get
        {
            var (x, y) = CentreErrorMm();
            return Math.Sqrt(x * x + y * y) < CentredMm;
        }
    }

    private static int FloorMod(int a, int m) => ((a % m) + m) % m;
    private static int FloorDiv(int a, int m) => (int)Math.Floor(a / (double)m);
}
