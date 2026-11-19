using Godot;

namespace CncTrainer.Procedural;

/// <summary>Where a lathe turret turns to for a tool, and how it gets
/// there. Engine-free.</summary>
public static class TurretIndex
{
    /// <summary>Turret angle (degrees) that brings this tool's station to
    /// the cutting position: station 1 at 180 (its insert pointing at the
    /// centreline), the others evenly round.</summary>
    public static float TargetAngleDeg(int toolNumber, int stationCount) =>
        180f - ((toolNumber - 1) % stationCount) * (360f / stationCount);

    /// <summary>Rate-limited step from `current` toward `target`, taking the
    /// shorter way round the 0/360 wrap.</summary>
    public static float StepToward(float current, float target, float maxStepDeg)
    {
        float diff = Mathf.Wrap(target - current, -180f, 180f);
        return current + Mathf.Clamp(diff, -maxStepDeg, maxStepDeg);
    }
}
