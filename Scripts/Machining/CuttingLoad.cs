using Godot;
using CncTrainer.Chips;

namespace CncTrainer.Machining;

/// <summary>What a cutting tool can take. Engine-free copy of the parts of
/// a ToolSpec the load model needs.</summary>
public readonly record struct ToolLimits(bool Lathe, bool Parting, float DiameterMm, float StickOutMm, int Flutes)
{
    // Transverse rupture strength of carbide with a working margin, N/mm^2.
    private const float BendingStressNPerMm2 = 2000f;

    /// <summary>Cutting force that snaps the tool: a round cutter bends
    /// like a cantilever (sigma = 32 F L / (pi d^3)); a lathe insert takes
    /// what its seat and edge take.</summary>
    public float BreakForceN => Lathe
        ? (Parting ? 2500f : 6000f)
        : BendingStressNPerMm2 * Mathf.Pi * DiameterMm * DiameterMm * DiameterMm / (32f * Mathf.Max(1f, StickOutMm));

    public bool Slender => !Lathe && StickOutMm > 4f * DiameterMm;
}

public enum CutFault { None, SpindleStopped, ToolOverload, SpindleOverload }

/// <summary>How hard one moment of cutting is on the spindle and the tool.
/// Loads are fractions of the limit (1 = at the limit).</summary>
public readonly record struct CutLoad(
    float PowerW, float SpindleLoad, float ForceN, float ToolLoad, float ChipLoadRatio, float SpeedRatio,
    bool Rubbing, bool Chatter, float WearPerSecond, CutFault Fault);

/// <summary>
/// Turns a material removal rate and the cutting conditions into spindle
/// load, tool load and tool wear. Engine-free.
///
/// Power = removal rate x the material's specific cutting energy. Cutting
/// force = power / cutting speed. Wear follows Taylor's tool-life law
/// (life ~ speed^(-1/n), n = 0.25 for carbide): at the material's recommended
/// speed a tool lasts ToolLifeSeconds, at twice that speed a sixteenth of it.
/// A training-scale model: the directions and the orders of magnitude are
/// right, the third digit is not.
/// </summary>
public static class CuttingLoad
{
    /// <summary>Tool life at the recommended speed and a sound chip load.
    /// Shorter than a real insert's so wear is seen within a session.</summary>
    public const float ToolLifeSeconds = 20f * 60f;
    public const float StallAtSpindleLoad = 1.5f;
    private const float TaylorExponent = 0.25f;
    private const float MinSpeedMmPerSec = 50f;

    /// <param name="removalRateMm3PerSec">Smoothed over a few samples.</param>
    /// <param name="feedPerRevMm">Tool travel per spindle turn.</param>
    /// <param name="wearFactor">1 = dry; coolant lowers it.</param>
    public static CutLoad Evaluate(float removalRateMm3PerSec, float rpm, float surfaceSpeedMmPerSec, float feedPerRevMm,
        ToolLimits tool, MaterialPreset material, float ratedPowerW, float wearFactor = 1f)
    {
        if (removalRateMm3PerSec <= 0f)
            return default;
        float power = removalRateMm3PerSec * material.SpecificEnergyJPerMm3;
        float spindleLoad = power / Mathf.Max(1f, ratedPowerW);
        if (Mathf.Abs(rpm) < 1f)
            return new CutLoad(power, spindleLoad, 0f, 2f, 0f, 0f, false, false, 0f, CutFault.SpindleStopped);

        // Facing to the centre of a bar takes the cutting speed to zero
        // together with the removal rate; the floor keeps the quotient sane there.
        float force = power / (Mathf.Max(surfaceSpeedMmPerSec, MinSpeedMmPerSec) / 1000f);
        float toolLoad = force / tool.BreakForceN;

        float chipLoad = tool.Lathe ? feedPerRevMm : feedPerRevMm / Mathf.Max(1, tool.Flutes);
        float recommendedChipLoad = tool.Lathe ? material.TurningFeedMmPerRev : material.ChipLoadPerDiameter * tool.DiameterMm;
        float chipLoadRatio = chipLoad / recommendedChipLoad;
        bool rubbing = chipLoadRatio < 0.25f;

        float speedRatio = surfaceSpeedMmPerSec * 0.06f / material.RecommendedSpeedMPerMin;
        float wear = wearFactor / ToolLifeSeconds
                     * Mathf.Clamp(Mathf.Pow(speedRatio, 1f / TaylorExponent), 0.2f, 60f)
                     * (rubbing ? 3f : 1f)
                     * (1f + 4f * Mathf.Max(0f, toolLoad - 0.5f));

        bool chatter = toolLoad > 0.75f || (tool.Slender && toolLoad > 0.35f);
        var fault = toolLoad > 1f ? CutFault.ToolOverload
            : spindleLoad > StallAtSpindleLoad ? CutFault.SpindleOverload
            : CutFault.None;
        return new CutLoad(power, spindleLoad, force, toolLoad, chipLoadRatio, speedRatio, rubbing, chatter, wear, fault);
    }
}
