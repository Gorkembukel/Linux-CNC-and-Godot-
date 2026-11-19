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

/// <summary>What sort of chuck it is.</summary>
public enum ChuckKind
{
    /// <summary>Three-jaw self-centring scroll chuck: quick, holds to a few hundredths.</summary>
    ThreeJaw,
    /// <summary>Four-jaw independent: each jaw on its own screw. Seats well
    /// off centre, but can be dialled in to a few microns, also when
    /// tightened.</summary>
    FourJawIndependent,
    /// <summary>Collet chuck: one collet per size, small bars only, near
    /// dead true; nothing to tap.</summary>
    Collet,
}

/// <summary>
/// State of a chuck and the round bar in it: what grips (jaws or a collet,
/// <see cref="ChuckJaws"/>), jaw opening, how hard it is tightened, whether
/// the key is still in it, and how far the bar sticks out. Lengths in mm.
/// Engine-free so it is unit-testable.
/// </summary>
public sealed class ChuckGrip
{
    public const int FullTorque = 3;
    public const float JawStepMm = 4f;
    /// <summary>Less bar than this inside the jaws does not hold.</summary>
    public const float MinGripMm = 15f;
    /// <summary>Soft jaws bored for one diameter hold bars this close to it.</summary>
    public const float SoftJawFitMm = 0.3f;

    public readonly ChuckDims Dims;
    public readonly ChuckJaws Jaws;
    public float MaxOpeningMm => Dims.MaxOpeningMm;
    public float JawDepthMm => Dims.JawDepthMm;
    /// <summary>Bars thinner than the spindle bore can go in deeper than the jaws.</summary>
    public float BoreMm => Dims.BoreMm;
    public float BoreDepthMm => Dims.BoreDepthMm;
    public ChuckKind Kind => Dims.Kind;
    public bool IsCollet => Jaws.IsCollet;

    /// <summary>Jaw opening (diameter, mm). A scroll chuck's comes from its
    /// jaws' radii (the innermost jaw); a reversed set grips further out by
    /// its outer step. Other chucks keep their own.</summary>
    public float OpeningMm => Jaws.Scroll && Jaws.Seated > 0 ? 2f * Jaws.MinRadiusMm + ReversedStepMm : _openingMm;
    private float _openingMm;
    /// <summary>How much further out the outer step of reversed jaws grips.</summary>
    private float ReversedStepMm => JawSet == JawKind.HardReversed ? MaxOpeningMm * 0.3f : 0f;
    /// <summary>How far one key notch moves the opening (diameter, mm).</summary>
    public float StepMm => Jaws.Scroll ? 2f * Dims.ScrollPitchMm / (Jaws.SlotCount * ChuckJaws.NotchesPerSlot) : JawStepMm;
    public int Torque { get; private set; }
    public bool KeyIn;
    public bool HasBar { get; private set; }
    public float BarDiameterMm { get; private set; }
    public float BarLengthMm { get; private set; }
    public float StickOutMm { get; private set; }
    /// <summary>The jaw set in the chuck (hard when none is in).</summary>
    public JawKind JawSet => Jaws.Set ?? JawKind.Hard;
    /// <summary>The diameter the soft jaws were bored for; null until they are.</summary>
    public float? SoftBoredForMm { get; private set; }
    /// <summary>The part is held by its bore: the jaws open out against it
    /// (ID gripping), leaving the whole outside free to turn.</summary>
    public bool Inside { get; private set; }
    /// <summary>The diameter the jaws bear on: the bar's outside, or its
    /// bore when held from inside.</summary>
    public float GripDiameterMm => Inside ? _boreMm : BarDiameterMm;
    private float _boreMm, _boreDepthMm;

    /// <summary>Smallest and largest bore the hard jaws open out into.</summary>
    public float MinBoreGripMm => MaxOpeningMm * 0.25f;
    public float MaxBoreGripMm => MaxOpeningMm;

    /// <summary>The jaws are on the part (closed onto it, or opened out
    /// into its bore): the key now tightens instead of moving them.</summary>
    public bool AtContact => HasBar && (IsCollet || Torque > 0 || (Inside ? OpeningMm + StepMm > _boreMm : OpeningMm - StepMm < BarDiameterMm));

    /// <summary>Smallest and largest diameter the jaws (or the collet) close on.</summary>
    public float MinGripDiameterMm => IsCollet ? (Jaws.ColletMm ?? 0f) - ChuckJaws.ColletRangeMm
        : JawSet == JawKind.HardReversed ? MaxOpeningMm * 0.5f : 0f;
    public float MaxGripDiameterMm => IsCollet ? Jaws.ColletMm ?? 0f
        : JawSet == JawKind.HardReversed ? MaxOpeningMm * 1.3f : MaxOpeningMm;

    public bool SoftJawsFit(float diameterMm) => SoftBoredForMm is float bored && Math.Abs(bored - diameterMm) <= SoftJawFitMm;

    /// <param name="fitted">Jaws already in the chuck; null = none (a collet chuck never has jaws).</param>
    public ChuckGrip(ChuckDims dims, JawKind? fitted = JawKind.Hard)
    {
        Dims = dims;
        Jaws = new ChuckJaws(dims, dims.Kind == ChuckKind.Collet ? null : fitted);
        _openingMm = Jaws.Complete ? dims.MaxOpeningMm * 0.75f : dims.MaxOpeningMm;
    }

    /// <summary>A three-jaw chuck with its hard jaws in.</summary>
    public ChuckGrip(float maxOpeningMm, float jawDepthMm, float boreMm, float boreDepthMm)
        : this(new ChuckDims(ChuckKind.ThreeJaw, 3, maxOpeningMm, jawDepthMm, boreMm, boreDepthMm))
    {
    }

    private float MaxInsertMm => Inside ? Math.Min(_boreDepthMm, JawDepthMm) : BarDiameterMm <= BoreMm ? BoreDepthMm : JawDepthMm;
    private float MinStickOutMm => Math.Max(0f, BarLengthMm - MaxInsertMm);

    public float GripLengthMm => HasBar ? Math.Min(BarLengthMm - StickOutMm, JawDepthMm) : 0f;
    public bool HoldsFirmly => HasBar && Torque >= 2 && GripLengthMm >= MinGripMm;
    /// <summary>How many diameters the bar sticks out of the jaws.</summary>
    public float Slenderness => HasBar && BarDiameterMm > 0f ? StickOutMm / BarDiameterMm : 0f;

    /// <summary>The collet is let go: a bar slides in and out of it.</summary>
    private bool ColletOpen => Torque == 0;

    public bool CanInsert(float diameterMm) => !HasBar && WhyNot(diameterMm) == null
        && (IsCollet ? ColletOpen : OpeningMm >= diameterMm + 1f);

    /// <summary>Why a bar of this diameter cannot go into the chuck, or null
    /// when it can (opening the jaws aside).</summary>
    public string WhyNot(float diameterMm) => IsCollet ? WhyNotCollet(diameterMm)
        : !Jaws.Complete ? $"Çeneler eksik: {Jaws.Seated}/{Jaws.SlotCount} takılı"
        : diameterMm > MaxGripDiameterMm ? $"Parça bu çenelere sığmaz (en fazla {MaxGripDiameterMm:0} mm)"
        : diameterMm < MinGripDiameterMm ? $"Ters çeneler bu kadar küçüğe kapanmaz (en az {MinGripDiameterMm:0} mm): sert çeneleri tak"
        : JawSet == JawKind.Soft && !SoftJawsFit(diameterMm) ? "Yumuşak çeneler bu çapa tornalanmadı"
        : null;

    private string WhyNotCollet(float diameterMm)
    {
        if (Jaws.ColletMm is not float collet)
            return "Pens aynasında pens yok: çubuğun ölçüsündeki pensi tak";
        if (diameterMm > collet)
            return System.FormattableString.Invariant($"Çubuk Ø{collet:0} pense girmez: Ø{Math.Ceiling(diameterMm):0} pensi tak");
        if (diameterMm < collet - ChuckJaws.ColletRangeMm)
            return System.FormattableString.Invariant($"Ø{collet:0} pens bu ince çubuğu tutmaz: Ø{Math.Ceiling(diameterMm):0} pensi tak");
        return null;
    }

    /// <summary>A part with this bore can be slid over the jaws to be held
    /// from inside: hard jaws, closed smaller than the bore.</summary>
    public bool CanInsertInside(float boreMm) => !HasBar && WhyNotInside(boreMm) == null && OpeningMm <= boreMm - 1f;

    /// <summary>Why a part cannot be held by a bore this size, or null when
    /// it can (closing the jaws aside).</summary>
    public string WhyNotInside(float boreMm) =>
        IsCollet ? "Pens aynası içten bağlamaz"
        : !Jaws.Complete ? $"Çeneler eksik: {Jaws.Seated}/{Jaws.SlotCount} takılı"
        : JawSet != JawKind.Hard ? "İçten bağlama sert çenelerle olur (normal yönde takılı)"
        : boreMm < MinBoreGripMm ? $"Delik çenelerin içine girmeye çok dar (en az Ø{MinBoreGripMm:0} mm)"
        : boreMm > MaxBoreGripMm ? $"Delik çenelerin açılabildiğinden geniş (en fazla Ø{MaxBoreGripMm:0} mm)"
        : null;

    /// <summary>Puts a part in held by its bore: slid over the closed jaws
    /// as far as the bore (or the jaws) allow.</summary>
    public bool InsertInside(float boreMm, float boreDepthMm, float outerDiameterMm, float lengthMm)
    {
        if (!CanInsertInside(boreMm))
            return false;
        Inside = true;
        _boreMm = boreMm;
        _boreDepthMm = boreDepthMm;
        HasBar = true;
        BarDiameterMm = outerDiameterMm;
        BarLengthMm = lengthMm;
        Torque = 0;
        StickOutMm = MinStickOutMm;
        SeatRunout();
        return true;
    }

    /// <summary>Puts a jaw into the slot the scroll is at (independent
    /// chuck: the next empty slot). Only with the key in and nothing in the
    /// chuck. Returns why not, or null when it went in.</summary>
    public string InstallJaw(JawKind kind, int number, ChuckKind madeFor, string family = "")
    {
        if (HasBar)
            return "Çene takmak için önce parçayı çıkar";
        if (!KeyIn)
            return "Çeneler ayna anahtarıyla takılır: önce anahtarı tak";
        if (Jaws.Install(kind, number, madeFor, family) is string why)
            return why;
        SoftBoredForMm = null;
        if (Jaws.Complete && !Jaws.Scroll)
            _openingMm = MaxGripDiameterMm;
        return null;
    }

    public string InstallCollet(float sizeMm) => HasBar ? "Önce çubuğu çıkar" : Jaws.InstallCollet(sizeMm);

    /// <summary>Takes the collet out (only with no bar in it); its size, or null.</summary>
    public float? TakeCollet() => HasBar ? null : Jaws.TakeCollet();

    /// <summary>Bores the soft jaws to hold this diameter (the operator
    /// clamps a ring in them and takes a light cut across their faces).</summary>
    public bool BoreSoftJaws(float diameterMm)
    {
        if (!Jaws.Complete || JawSet != JawKind.Soft || HasBar || diameterMm > MaxGripDiameterMm)
            return false;
        SoftBoredForMm = diameterMm;
        if (!Jaws.Scroll)
            _openingMm = Math.Max(_openingMm, diameterMm + 2f);
        return true;
    }

    /// <summary>Puts a bar in, pushed back as far as the jaws (or the bore) allow.</summary>
    public bool Insert(float diameterMm, float lengthMm)
    {
        if (!CanInsert(diameterMm))
            return false;
        HasBar = true;
        Inside = false;
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
        // Jaws bored in place for this very diameter hold it nearly true; a
        // collet nearly so; four independent jaws seat it well off until dialled in.
        bool bored = JawSet == JawKind.Soft && SoftBoredForMm != null;
        float scale = Kind switch
        {
            ChuckKind.Collet => 0.05f,
            ChuckKind.FourJawIndependent => 6f,
            _ => bored ? 0.12f : 1f,
        };
        float e = scale * (0.015f + 0.045f * (float)_rng.NextDouble()), t = scale * 0.02f * (float)_rng.NextDouble();
        // Jaws put in out of order hold off by a share of the scroll pitch;
        // jaws bored in place take that out with them.
        var (ex, ey) = bored ? (0f, 0f) : Jaws.CentreErrorMm();
        OffsetMm = (e * (float)Math.Cos(a) + ex, e * (float)Math.Sin(a) + ey);
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
        if (IsCollet)
            return "Pens kendini merkezler: vurmakla oynamaz";
        if (Kind == ChuckKind.FourJawIndependent)
            return AdjustJaw(angle);
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

    /// <summary>Four-jaw chuck: the jaw on this side backed off and the
    /// opposite one taken up, a turn of the key each, at any tightness.</summary>
    private string AdjustJaw(float angle)
    {
        if (Torque < 1)
            return "Bağımsız ayna: önce dört çeneyi de hafif sık";
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        float high = OffsetMm.X * cos + OffsetMm.Y * sin;
        float push = high * (0.85f + 0.1f * (float)_rng.NextDouble());
        OffsetMm = (OffsetMm.X - cos * push, OffsetMm.Y - sin * push);
        return high > 0f ? "Bu taraftaki çene gevşetildi, karşısı sıkıldı: parça merkeze yaklaştı"
            : "Alçak taraftaki çene ayarlandı: salgı arttı";
    }

    public void Remove()
    {
        HasBar = false;
        Inside = false;
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

    /// <summary>The jaws are as far open as they go while still held: turning
    /// back further lets them go, the last one caught first.</summary>
    public bool FullyOpen => !HasBar && Torque == 0 && (Jaws.Scroll
        ? Jaws.Seated == 0 || Jaws.Notch <= Jaws.LastCatchNotch
        : !Jaws.Complete || _openingMm >= MaxGripDiameterMm);

    /// <summary>One notch of the key. Closing (+1) runs the jaws in until they
    /// meet the bar, then tightens; holding a bore it opens them out against
    /// it, then tightens; a collet is only tightened or let go. On a scroll
    /// chuck every notch turns the scroll: it catches a loose jaw as its
    /// thread start crosses that slot, and turned back it lets go of any jaw
    /// past where it was caught (it lies loose in its slot).</summary>
    public ScrollEvent TurnKey(int steps)
    {
        if (IsCollet)
        {
            if (steps > 0 && HasBar)
                Torque = Math.Min(FullTorque, Torque + 1);
            else if (steps < 0 && Torque > 0)
                Torque--;
            return default;
        }
        return Jaws.Scroll ? TurnScroll(steps) : TurnScrews(steps);
    }

    private ScrollEvent TurnScroll(int steps)
    {
        if (Inside)
        {
            // Opening out against the bore is turning the scroll back.
            if (steps > 0 && Torque == 0 && OpeningMm + StepMm <= _boreMm)
                return Jaws.Advance(-1);
            if (steps > 0)
                Torque = Math.Min(FullTorque, Torque + 1);
            else if (Torque > 0)
                Torque--;
            else
                return Jaws.Advance(1);
            return default;
        }
        if (steps > 0)
        {
            if (HasBar && Jaws.Seated > 0 && 2f * Jaws.MinRadiusAfterMm(1) + ReversedStepMm < BarDiameterMm)
            {
                Torque = Math.Min(FullTorque, Torque + 1);
                return default;
            }
            // Jaws closed right in stop the key.
            if (Jaws.Seated > 0 && Jaws.MinRadiusAfterMm(1) < 0.5f)
                return default;
            var caught = Jaws.Advance(1);
            if (caught.Caught != null)
                SoftBoredForMm = null;
            return caught;
        }
        if (Torque > 0)
        {
            Torque--;
            return default;
        }
        if (HasBar && Jaws.Seated > 0 && Jaws.Notch - 1 < Jaws.LastCatchNotch)
            return default; // would let go of a jaw holding the bar
        var released = Jaws.Advance(-1);
        if (released.Released != null)
            SoftBoredForMm = null;
        return released;
    }

    /// <summary>An independent chuck: the key works each jaw's own screw.</summary>
    private ScrollEvent TurnScrews(int steps)
    {
        if (Inside)
        {
            if (steps > 0 && _openingMm < _boreMm)
                _openingMm = Math.Min(_boreMm, _openingMm + JawStepMm);
            else if (steps > 0)
                Torque = Math.Min(FullTorque, Torque + 1);
            else if (Torque > 0)
                Torque--;
            else
                _openingMm = Math.Max(MinGripDiameterMm, _openingMm - JawStepMm);
            return default;
        }
        float contact = HasBar ? BarDiameterMm : 0f;
        if (steps > 0)
        {
            if (!Jaws.Complete)
                return default;
            if (_openingMm > contact)
                _openingMm = Math.Max(contact, _openingMm - JawStepMm);
            else if (HasBar)
                Torque = Math.Min(FullTorque, Torque + 1);
            return default;
        }
        if (Torque > 0)
            Torque--;
        else if (FullyOpen && !HasBar)
            return new ScrollEvent(null, Jaws.ReleaseLast());
        else
            _openingMm = Math.Min(MaxGripDiameterMm, _openingMm + JawStepMm);
        return default;
    }
}
