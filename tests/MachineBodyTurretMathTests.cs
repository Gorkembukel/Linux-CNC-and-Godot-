using CncTrainer;

namespace CncTrainer.Tests;

/// <summary>Only MachineBody's two pure-math turret-angle helpers are
/// exercised here -- actual mesh/turret building is Godot-engine-dependent
/// and verified live only, same as the rest of MachineBody.cs always has
/// been (it's never had unit tests).</summary>
public class MachineBodyTurretMathTests
{
    [Fact]
    public void TargetIndexAngleDeg_PutsEachToolsStationFacingTheCenterline()
    {
        Assert.Equal(180f, MachineBody.TargetIndexAngleDeg(1, 3), precision: 3);
        Assert.Equal(60f, MachineBody.TargetIndexAngleDeg(2, 3), precision: 3);
        Assert.Equal(-60f, MachineBody.TargetIndexAngleDeg(3, 3), precision: 3);
    }

    [Fact]
    public void StepAngleToward_NeverOvershootsTheTarget()
    {
        // 170 (not 180) deg apart -- exactly 180 is an equally-valid-either-
        // direction tie, not a meaningful case to pin a direction on.
        float result = MachineBody.StepAngleToward(current: 0f, target: 170f, maxStepDeg: 10f);
        Assert.Equal(10f, result, precision: 3);
    }

    [Fact]
    public void StepAngleToward_SnapsExactlyOnTarget_WhenWithinOneStep()
    {
        float result = MachineBody.StepAngleToward(current: 175f, target: 180f, maxStepDeg: 10f);
        Assert.Equal(180f, result, precision: 3);
    }

    [Fact]
    public void StepAngleToward_TakesTheShorterWayAround_WhenWrappingPast0Or360()
    {
        // From 10 deg to 350 deg: the short way is -20 (through 0), not +340.
        float result = MachineBody.StepAngleToward(current: 10f, target: 350f, maxStepDeg: 100f);
        Assert.Equal(-10f, result, precision: 3);
    }
}
