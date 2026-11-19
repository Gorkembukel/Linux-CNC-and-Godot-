using System;
using CncTrainer.Chips;

namespace CncTrainer.Machining;

/// <summary>
/// Chips that cannot get out of the cut. A cutter buried deeper than its own
/// diameter in a pocket or slot has nowhere to throw them: without coolant
/// washing them out they pile up, a ductile material (aluminium, stainless,
/// plastics) smears them into the flutes, and the tool is recutting its own
/// chips until it snaps. Coolant clears them fast; pausing the cut lets
/// them fall out slowly. Engine-free.
/// </summary>
public sealed class ChipEvacuation
{
    public enum Verdict { Clear, Building, Packed }

    /// <summary>Cutters this many diameters deep or more cannot clear their chips.</summary>
    public const float DeepCutDiameters = 1f;
    /// <summary>Only materials at least this ductile smear into the flutes.</summary>
    public const float SmearingDuctility = 0.7f;
    /// <summary>Packed chips that snap the tool (mm^3).</summary>
    public const float PackedMm3 = 300f;
    private const float ShareTrapped = 0.25f, ClearDryMm3PerSec = 30f, ClearWetMm3PerSec = 500f;

    public float TrappedMm3 { get; private set; }

    /// <summary>One frame of cutting. depthDiameters: axial depth of cut
    /// over the cutter's diameter (0 on a lathe, where chips fly free).</summary>
    public Verdict Track(float dt, float removalMm3PerSec, float depthDiameters, MaterialPreset material, bool coolant)
    {
        if (!coolant && depthDiameters >= DeepCutDiameters && material.Ductility >= SmearingDuctility)
            TrappedMm3 += removalMm3PerSec * ShareTrapped * dt;
        else
            Clear(dt, coolant);
        if (TrappedMm3 >= PackedMm3)
        {
            TrappedMm3 = 0f;
            return Verdict.Packed;
        }
        return TrappedMm3 >= PackedMm3 / 2f ? Verdict.Building : Verdict.Clear;
    }

    /// <summary>Not cutting: the chips fall or wash out.</summary>
    public void Clear(float dt, bool coolant) =>
        TrappedMm3 = Math.Max(0f, TrappedMm3 - dt * (coolant ? ClearWetMm3PerSec : ClearDryMm3PerSec));
}
