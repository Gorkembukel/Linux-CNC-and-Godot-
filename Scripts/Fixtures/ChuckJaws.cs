using System;
using System.Linq;

namespace CncTrainer.Fixtures;

/// <summary>A chuck's own sizes, whatever lathe it is on (mm). Engine-free;
/// <see cref="ChuckModel"/> is the editable resource behind it.</summary>
/// <param name="ScrollPitchMm">Scroll chucks: how far a jaw moves per turn
/// of the scroll, so how far off a jaw engaged a turn late sits.</param>
public readonly record struct ChuckDims(ChuckKind Kind, int JawCount, float MaxOpeningMm, float JawDepthMm,
    float BoreMm, float BoreDepthMm, float ScrollPitchMm = 6f);

/// <summary>A jaw as it sits in a slot: which set and number it is, and on
/// which turn of the scroll it caught.</summary>
public readonly record struct SeatedJaw(JawKind Kind, int Number, int Turn);

/// <summary>
/// What a chuck grips with, and how it gets there. A scroll chuck's jaws go
/// in one at a time, numbered: the key turns the scroll until its thread
/// starts at a slot, the jaw with that slot's number goes in, the key
/// carries on to the next slot. A jaw in the wrong slot, or one caught a
/// turn late, sits off and the chuck holds off centre. An independent
/// chuck's jaws each have their own screw: any slot, any order. A collet
/// chuck takes one collet, made for one size. Engine-free.
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
    public int Notch { get; private set; }
    /// <summary>The collet in a collet chuck (its size), null when empty.</summary>
    public float? ColletMm { get; private set; }

    public bool IsCollet => Dims.Kind == ChuckKind.Collet;
    public bool Independent => Dims.Kind == ChuckKind.FourJawIndependent;
    public int SlotCount => _slots.Length;
    public SeatedJaw? Slot(int index) => _slots[index];
    public int Seated => _slots.Count(s => s != null);
    public bool Complete => !IsCollet && Seated == _slots.Length;
    public bool Empty => IsCollet ? ColletMm == null : Seated == 0;
    /// <summary>Ready to hold a part: all jaws in, or a collet in.</summary>
    public bool Ready => IsCollet ? ColletMm != null : Complete;
    /// <summary>The set the jaws belong to (the first one's, they cannot mix).</summary>
    public JawKind? Set => _slots.FirstOrDefault(s => s != null)?.Kind;
    /// <summary>The slot the scroll's thread starts at now (0-based).</summary>
    public int StartSlot => FloorMod(Notch, _slots.Length * NotchesPerSlot) / NotchesPerSlot;
    private int ScrollTurn => FloorDiv(Notch, _slots.Length * NotchesPerSlot);

    /// <param name="fitted">The set already in, correctly; null = no jaws (or no collet).</param>
    public ChuckJaws(ChuckDims dims, JawKind? fitted = JawKind.Hard)
    {
        Dims = dims;
        _slots = new SeatedJaw?[dims.Kind == ChuckKind.Collet ? 0 : Math.Max(2, dims.JawCount)];
        if (fitted is JawKind kind)
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = new SeatedJaw(kind, i + 1, 0);
    }

    /// <summary>The key turns the scroll (independent: nothing to turn).</summary>
    public void Advance(int notches) => Notch += notches;

    /// <summary>The slot the next jaw goes into: on a scroll chuck the one
    /// the thread starts at, on an independent one the first empty one;
    /// null when that slot is taken.</summary>
    public int? OpenSlot => Independent
        ? Array.FindIndex(_slots, s => s == null) is var i && i >= 0 ? i : null
        : _slots[StartSlot] == null ? StartSlot : null;

    /// <summary>Puts a jaw in. Returns why not, or null when it went in.</summary>
    public string Install(JawKind kind, int number, ChuckKind madeFor)
    {
        if (IsCollet)
            return "Pens aynasına çene takılmaz";
        if (madeFor != Dims.Kind)
            return "Bu çene bu aynanın değil";
        if (Set is JawKind set && set != kind)
            return "Farklı takım çeneler karıştırılmaz: önce takılı olanları sök";
        if (_slots.Any(s => s?.Number == number))
            return $"{number} numaralı çene zaten takılı";
        if (OpenSlot is not int slot)
            return Independent ? "Bütün yuvalar dolu" : $"Spiralin başı {StartSlot + 1}. yuvada ve orası dolu: anahtarı çevir";
        _slots[slot] = new SeatedJaw(kind, number, ScrollTurn);
        return null;
    }

    /// <summary>The jaw the scroll has just let go of while opening past the
    /// end (independent: the last one in), or null when none comes out here.</summary>
    public SeatedJaw? Release()
    {
        int slot = Independent ? Array.FindLastIndex(_slots, s => s != null) : StartSlot;
        if (slot < 0 || _slots[slot] is not SeatedJaw jaw)
            return null;
        _slots[slot] = null;
        return jaw;
    }

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

    /// <summary>How far out each slot's jaw sits against a correct fit (mm):
    /// a pitch per turn late, a share of the pitch per slot it is out of
    /// its numbered place. Zero for independent jaws (each set by hand).</summary>
    public float ErrorMm(int slot)
    {
        if (Independent || _slots[slot] is not SeatedJaw jaw)
            return 0f;
        float pitch = Dims.ScrollPitchMm;
        return jaw.Turn * pitch + (jaw.Number - (slot + 1)) * pitch / _slots.Length;
    }

    /// <summary>Where the jaws' centre sits off the spindle's because of
    /// how they went in (mm): a jaw further out lets the part over to it.</summary>
    public (float X, float Y) CentreErrorMm()
    {
        if (!Complete || Independent)
            return (0f, 0f);
        int n = _slots.Length;
        float mean = Enumerable.Range(0, n).Average(ErrorMm);
        float x = 0f, y = 0f;
        for (int i = 0; i < n; i++)
        {
            double a = Math.Tau * i / n;
            float e = ErrorMm(i) - mean;
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
