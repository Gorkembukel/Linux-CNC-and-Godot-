using Godot;

namespace CncTrainer.Material;

/// <summary>Result of sweeping the tool between two CONFIRMED samples: which part
/// of the stock changed (for partial mesh regen) and the derived chip inputs
/// for Phase 3 (LinuxCNC gives us no chip data at all -- this is where cutting
/// parameters actually come from).</summary>
public readonly struct SweepResult
{
    public readonly bool MaterialRemoved;
    public readonly int MinDirtyIndex, MaxDirtyIndex;
    public readonly float DepthOfCutMm;
    public readonly float ContactLengthMm;

    /// <summary>Set when this sweep drove a bucket's radius from positive down
    /// to ~0 for the first time -- i.e. the stock was just cut all the way
    /// through at that Z (a parting cut). The lowest such index, if more than
    /// one bucket crossed to zero in this one sweep (a wide tool/step).</summary>
    public readonly int? SeveredIndex;
    public readonly float VolumeMm3;

    public SweepResult(bool removed, int minIdx, int maxIdx, float depth, float contactLength, int? severedIndex = null, float volumeMm3 = 0f)
    {
        VolumeMm3 = volumeMm3;
        MaterialRemoved = removed;
        MinDirtyIndex = minIdx;
        MaxDirtyIndex = maxIdx;
        DepthOfCutMm = depth;
        ContactLengthMm = contactLength;
        SeveredIndex = severedIndex;
    }

    public static readonly SweepResult None = new(false, 0, 0, 0, 0);
}

/// <summary>
/// Abstraction over "what shape is the stock, and how does the tool cut it."
/// LatheRadiusProfile implements this now (brief section 1's 2-axis lathe
/// target); a future mill HeightmapModel would implement it too, so the rest of
/// the app (mesh building, chip system) only needs to know this interface.
/// </summary>
public interface IMaterialModel
{
    /// <summary>Sweep the tool's swept volume between two CONFIRMED positions
    /// only -- never predicted ones (see PLAN.md section 3's hard rule).</summary>
    SweepResult Sweep(Vector2 fromPos, Vector2 toPos, float toolNoseRadius, float feedPerRev, float spindleRpm);
}
