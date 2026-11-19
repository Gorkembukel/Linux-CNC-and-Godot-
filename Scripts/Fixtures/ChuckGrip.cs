using System;

namespace CncTrainer.Fixtures;

/// <summary>The set of jaws on a chuck.</summary>
public enum JawKind
{
    /// <summary>Hardened stepped jaws, as delivered: grip from small bars up to the chuck's capacity.</summary>
    Hard,
    /// <summary>The hard jaws turned round: their outer steps reach large
    /// diameters, but they cannot close on small bars.</summary>
    HardReversed,
    /// <summary>Soft (mild steel / aluminium) jaws: they only hold a part
    /// once bored to its diameter, and then hold it almost dead true.</summary>
    Soft,
}

/// <summary>
/// State of a jaw chuck and the round bar in it: jaw opening, how hard it is
/// tightened, whether the key is still in it, and how far the bar sticks
/// out. Lengths in mm. Engine-free so it is unit-testable.
/// </summary>
public sealed class ChuckGrip
{
    public const int FullTorque = 3;
    public const float JawStepMm = 4f;
    /// <summary>Less bar than this inside the jaws does not hold.</summary>
    public const float MinGripMm = 15f;
    /// <summary>Soft jaws bored for one diameter hold bars this close to it.</summary>
    public const float SoftJawFitMm = 0.3f;

    public readonly float MaxOpeningMm;
    public readonly float JawDepthMm;
    /// <summary>Bars thinner than the spindle bore can go in deeper than the jaws.</summary>
    public readonly float BoreMm;
    public readonly float BoreDepthMm;

    public float OpeningMm { get; private set; }
    public int Torque { get; private set; }
    public bool KeyIn;
    public bool HasBar { get; private set; }
    public float BarDiameterMm { get; private set; }
    public float BarLengthMm { get; private set; }
    public float StickOutMm { get; private set; }
    public JawKind Jaws { get; private set; } = JawKind.Hard;
    /// <summary>The diameter the soft jaws were bored for; null until they are.</summary>
    public float? SoftBoredForMm { get; private set; }

    /// <summary>Smallest and largest diameter the mounted jaws close on.</summary>
    public float MinGripDiameterMm => Jaws == JawKind.HardReversed ? MaxOpeningMm * 0.5f : 0f;
    public float MaxGripDiameterMm => Jaws == JawKind.HardReversed ? MaxOpeningMm * 1.3f : MaxOpeningMm;

    public bool SoftJawsFit(float diameterMm) => SoftBoredForMm is float bored && Math.Abs(bored - diameterMm) <= SoftJawFitMm;

    public ChuckGrip(float maxOpeningMm, float jawDepthMm, float boreMm, float boreDepthMm)
    {
        MaxOpeningMm = maxOpeningMm;
        JawDepthMm = jawDepthMm;
        BoreMm = boreMm;
        BoreDepthMm = boreDepthMm;
        OpeningMm = maxOpeningMm * 0.75f;
    }

    private float MaxInsertMm => BarDiameterMm <= BoreMm ? BoreDepthMm : JawDepthMm;
    private float MinStickOutMm => Math.Max(0f, BarLengthMm - MaxInsertMm);

    public float GripLengthMm => HasBar ? Math.Min(BarLengthMm - StickOutMm, JawDepthMm) : 0f;
    public bool HoldsFirmly => HasBar && Torque >= 2 && GripLengthMm >= MinGripMm;
    /// <summary>How many diameters the bar sticks out of the jaws.</summary>
    public float Slenderness => HasBar && BarDiameterMm > 0f ? StickOutMm / BarDiameterMm : 0f;

    public bool CanInsert(float diameterMm) => !HasBar && diameterMm >= MinGripDiameterMm && diameterMm <= MaxGripDiameterMm
        && OpeningMm >= diameterMm + 1f && (Jaws != JawKind.Soft || SoftJawsFit(diameterMm));

    /// <summary>Why a bar of this diameter cannot go into the mounted jaws,
    /// or null when it can (opening the jaws aside).</summary>
    public string WhyNot(float diameterMm) =>
        diameterMm > MaxGripDiameterMm ? $"Parça bu çenelere sığmaz (en fazla {MaxGripDiameterMm:0} mm)"
        : diameterMm < MinGripDiameterMm ? $"Ters çeneler bu kadar küçüğe kapanmaz (en az {MinGripDiameterMm:0} mm): sert çeneleri tak"
        : Jaws == JawKind.Soft && !SoftJawsFit(diameterMm) ? "Yumuşak çeneler bu çapa tornalanmadı"
        : null;

    /// <summary>Swaps the jaw set; only with the key in and nothing in the
    /// chuck. Returns why not, or null when done.</summary>
    public string ChangeJaws(JawKind kind)
    {
        if (HasBar)
            return "Çene değiştirmek için önce parçayı çıkar";
        if (!KeyIn)
            return "Çeneler ayna anahtarıyla sökülür: önce anahtarı tak";
        Jaws = kind;
        SoftBoredForMm = null;
        OpeningMm = Math.Clamp(OpeningMm, MinGripDiameterMm, MaxGripDiameterMm);
        return null;
    }

    /// <summary>Bores the soft jaws to hold this diameter (the operator
    /// clamps a ring in them and takes a light cut across their faces).</summary>
    public bool BoreSoftJaws(float diameterMm)
    {
        if (Jaws != JawKind.Soft || HasBar || diameterMm > MaxGripDiameterMm)
            return false;
        SoftBoredForMm = diameterMm;
        OpeningMm = Math.Max(OpeningMm, diameterMm + 2f);
        return true;
    }

    /// <summary>Puts a bar in, pushed back as far as the jaws (or the bore) allow.</summary>
    public bool Insert(float diameterMm, float lengthMm)
    {
        if (!CanInsert(diameterMm))
            return false;
        HasBar = true;
        BarDiameterMm = diameterMm;
        BarLengthMm = lengthMm;
        Torque = 0;
        StickOutMm = Math.Max(MinStickOutMm, lengthMm - JawDepthMm);
        SeatRunout();
        return true;
    }

    private readonly Random _rng = new();

    /// <summary>How far the bar's axis sits off the spindle's at the jaws (mm),
    /// in the bar's own frame, and how much further per 100 mm out (a
    /// three-jaw chuck never holds dead true).</summary>
    public (float X, float Y) OffsetMm { get; private set; }
    public (float X, float Y) TiltPer100Mm { get; private set; }

    private void SeatRunout()
    {
        double a = _rng.NextDouble() * Math.Tau, b = _rng.NextDouble() * Math.Tau;
        // Jaws bored in place for this very diameter hold it nearly true.
        float scale = Jaws == JawKind.Soft ? 0.12f : 1f;
        float e = scale * (0.015f + 0.045f * (float)_rng.NextDouble()), t = scale * 0.02f * (float)_rng.NextDouble();
        OffsetMm = (e * (float)Math.Cos(a), e * (float)Math.Sin(a));
        TiltPer100Mm = (t * (float)Math.Cos(b), t * (float)Math.Sin(b));
    }

    /// <summary>More offset along the one there is (a burr under a jaw).</summary>
    public void AddRunout(float mm)
    {
        float length = (float)Math.Sqrt(OffsetMm.X * OffsetMm.X + OffsetMm.Y * OffsetMm.Y);
        float scale = length > 1e-6f ? (length + mm) / length : 1f;
        OffsetMm = length > 1e-6f ? (OffsetMm.X * scale, OffsetMm.Y * scale) : (mm, 0f);
    }

    private (float X, float Y) OffsetAt(float outMm) =>
        (OffsetMm.X + TiltPer100Mm.X * outMm / 100f, OffsetMm.Y + TiltPer100Mm.Y * outMm / 100f);

    /// <summary>A dial indicator outMm from the jaws, touching the bar from
    /// the direction at angle (rad, bar frame): how far the surface stands
    /// out toward it.</summary>
    public float Needle(float outMm, float angle)
    {
        var o = OffsetAt(outMm);
        return o.X * (float)Math.Cos(angle) + o.Y * (float)Math.Sin(angle);
    }

    /// <summary>Total indicator reading over a turn outMm from the jaws.</summary>
    public float RunoutMm(float outMm)
    {
        var o = OffsetAt(outMm);
        return 2f * (float)Math.Sqrt(o.X * o.X + o.Y * o.Y);
    }

    /// <summary>A mallet tap on the bar from the direction at angle (bar
    /// frame). Only a lightly tightened chuck lets the bar move: on its high
    /// side it goes toward the centre, on the low side further off.
    /// Returns what happened.</summary>
    public string Tap(float angle)
    {
        if (!HasBar)
            return null;
        if (Torque >= FullTorque)
            return "Ayna tam sıkı: vurmakla oynamaz, bir kademe gevşet";
        if (Torque < 2)
            return "Ayna gevşek: önce hafif sık, sonra vur";
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        float high = OffsetMm.X * cos + OffsetMm.Y * sin;
        float push = high > 0f ? high * (0.5f + 0.2f * (float)_rng.NextDouble()) : 0.01f;
        OffsetMm = (OffsetMm.X - cos * push, OffsetMm.Y - sin * push);
        return high > 0f ? "Tık: parça merkeze yaklaştı" : "Tık: alçak taraftan vuruldu, salgı arttı";
    }

    public void Remove()
    {
        HasBar = false;
        Torque = 0;
    }

    /// <summary>Pushes the bar in (negative) or pulls it out; only while the jaws are loose.</summary>
    public void Slide(float mm)
    {
        if (HasBar && Torque == 0)
            StickOutMm = Math.Clamp(StickOutMm + mm, MinStickOutMm, Math.Max(MinStickOutMm, BarLengthMm - 2f));
    }

    /// <summary>The free end has been cut off: the bar is now this long.</summary>
    public void SetLength(float lengthMm)
    {
        StickOutMm = Math.Max(0f, StickOutMm - (BarLengthMm - lengthMm));
        BarLengthMm = lengthMm;
    }

    /// <summary>One notch of the key: +1 closes the jaws, then tightens.</summary>
    public void TurnKey(int steps)
    {
        float contact = HasBar ? BarDiameterMm : 0f;
        if (steps > 0)
        {
            if (OpeningMm > contact)
                OpeningMm = Math.Max(contact, OpeningMm - JawStepMm);
            else if (HasBar)
                Torque = Math.Min(FullTorque, Torque + 1);
        }
        else if (Torque > 0)
            Torque--;
        else
            OpeningMm = Math.Min(MaxGripDiameterMm, OpeningMm + JawStepMm);
    }
}
