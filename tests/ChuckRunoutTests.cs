using System;
using System.Linq;
using CncTrainer.Fixtures;

namespace CncTrainer.Tests;

public class ChuckRunoutTests
{
    private static ChuckGrip Chucked(int torque)
    {
        var grip = new ChuckGrip(maxOpeningMm: 80f, jawDepthMm: 30f, boreMm: 30f, boreDepthMm: 250f);
        grip.Insert(50f, 140f);
        for (int i = 0; i < 60 && !grip.AtContact; i++)
            grip.TurnKey(1);
        for (int i = 0; i < torque; i++)
            grip.TurnKey(1);
        return grip;
    }

    private static (float Min, float Max) OneTurn(ChuckGrip grip, float outMm)
    {
        var readings = Enumerable.Range(0, 360).Select(d => grip.Needle(outMm, d * MathF.PI / 180f)).ToArray();
        return (readings.Min(), readings.Max());
    }

    [Fact]
    public void ABarNeverRunsDeadTrue_AndTheNeedleSweepIsTheRunout()
    {
        var grip = Chucked(3);
        float tir = grip.RunoutMm(0f);
        Assert.InRange(tir, 0.03f, 0.121f);
        var (min, max) = OneTurn(grip, 60f);
        Assert.Equal(grip.RunoutMm(60f), max - min, precision: 3);
    }

    [Fact]
    public void TappingTheHighSide_OfASnugChuck_BringsItIn_TightOrLowSideDoesNot()
    {
        var snug = Chucked(2);
        float before = snug.RunoutMm(0f);
        float high = Enumerable.Range(0, 360).Select(d => d * MathF.PI / 180f).OrderByDescending(a => snug.Needle(0f, a)).First();
        snug.Tap(high);
        Assert.True(snug.RunoutMm(0f) < before * 0.6f);

        var low = Chucked(2);
        float lowBefore = low.RunoutMm(0f);
        float lowSide = Enumerable.Range(0, 360).Select(d => d * MathF.PI / 180f).OrderBy(a => low.Needle(0f, a)).First();
        low.Tap(lowSide);
        Assert.True(low.RunoutMm(0f) > lowBefore);

        var tight = Chucked(3);
        float tightBefore = tight.RunoutMm(0f);
        tight.Tap(high);
        Assert.Equal(tightBefore, tight.RunoutMm(0f));
    }
}
