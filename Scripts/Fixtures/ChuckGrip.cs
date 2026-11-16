using System;

namespace CncTrainer.Fixtures;

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

    public bool CanInsert(float diameterMm) => !HasBar && diameterMm <= MaxOpeningMm && OpeningMm >= diameterMm + 1f;

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
        return true;
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
            OpeningMm = Math.Min(MaxOpeningMm, OpeningMm + JawStepMm);
    }
}
