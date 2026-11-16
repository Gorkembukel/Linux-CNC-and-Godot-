using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class LatheRadiusProfileTests
{
    [Fact]
    public void Sweep_RemovesMaterial_OnlyWithinTheSweptZRange()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 30f, zMin: -70f, zMax: 5f, stepMm: 0.5f);

        var result = profile.Sweep(
            fromPos: new Vector2(20f, 0f), toPos: new Vector2(20f, -60f),
            toolNoseRadius: 0.5f, feedPerRev: 0.2f, spindleRpm: 800f);

        Assert.True(result.MaterialRemoved);
        Assert.Equal(60f, result.ContactLengthMm, precision: 3);
        Assert.Equal(30f - 20.5f, result.DepthOfCutMm, precision: 3);

        // Inside the cut: radius dropped to programmed X + nose radius.
        Assert.Equal(20.5f, profile.RadiusAt(profile.IndexAt(-30f)), precision: 3);
        Assert.Equal(20.5f, profile.RadiusAt(profile.IndexAt(0f)), precision: 3);
        Assert.Equal(20.5f, profile.RadiusAt(profile.IndexAt(-60f)), precision: 3);

        // Outside the cut: untouched stock radius.
        Assert.Equal(30f, profile.RadiusAt(profile.IndexAt(-65f)), precision: 3);
        Assert.Equal(30f, profile.RadiusAt(profile.IndexAt(3f)), precision: 3);
    }

    [Fact]
    public void Sweep_NeverIncreasesRadius_EvenIfProgrammedPathIsWider()
    {
        // A "cut" that's actually wider than the existing stock (e.g. an air
        // move, or a retract) must never add material back.
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -10f, zMax: 10f, stepMm: 1f);
        profile.Sweep(new Vector2(20f, -5f), new Vector2(20f, 5f), 0f, 0f, 0f); // cut to 20

        var result = profile.Sweep(new Vector2(26f, -5f), new Vector2(26f, 5f), 0f, 0f, 0f); // "wider" move

        Assert.False(result.MaterialRemoved);
        Assert.Equal(20f, profile.RadiusAt(profile.IndexAt(0f)), precision: 3);
    }

    [Fact]
    public void Sweep_HandlesFacingMoves_WithNoZTravel()
    {
        // A pure facing/parting cut: Z stays fixed, X decreases.
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -10f, zMax: 10f, stepMm: 1f);

        var result = profile.Sweep(new Vector2(25f, 2f), new Vector2(-0.5f, 2f), 0f, 0.1f, 1000f);

        Assert.True(result.MaterialRemoved);
        Assert.Equal(0f, result.ContactLengthMm, precision: 3); // no Z travel
        // Radius clamps at 0 (can't go negative) even though the program drove
        // 0.5mm past centerline, as real facing/parting programs do.
        Assert.Equal(0f, profile.RadiusAt(profile.IndexAt(2f)), precision: 3);
    }

    [Fact]
    public void FullDemoRoughingSequence_ProducesExpectedFinalProfile()
    {
        // Mirrors linuxcnc-config/nc_files/demo_facing_turning.ngc's 4 roughing
        // passes (25 -> 23 -> 21 -> 20.2mm radius, Z2 to Z-60) plus the finish
        // pass to 20.0mm -- the brief's "known program and its expected final
        // profile" test. Tool nose radii match sim_lathe.tbl (T1=0.8, T2=0.4).
        var profile = new LatheRadiusProfile(stockRadiusMm: 26f, zMin: -65f, zMax: 5f, stepMm: 0.5f);

        foreach (float passRadius in new[] { 25f, 23f, 21f, 20.2f })
            profile.Sweep(new Vector2(passRadius, 2f), new Vector2(passRadius, -60f), 0.8f, 0.15f, 800f);
        profile.Sweep(new Vector2(20.5f, 2f), new Vector2(20.5f, -60f), 0.4f, 0.08f, 1200f);

        // Final radius along the turned section should be the finishing pass's
        // result: 20.5 + 0.4 nose radius = 20.9... but the roughing pass at
        // 20.2+0.8=21.0 was already smaller going in, so min() keeps whichever
        // pass actually cut deepest at each point -- here, the LAST (finish)
        // pass's 20.9 is wider than the deepest rough pass's 21.0, so min()
        // correctly keeps the finish pass's value only where it's smaller.
        float expectedFinal = Mathf.Min(20.2f + 0.8f, 20.5f + 0.4f); // = 20.9
        Assert.Equal(expectedFinal, profile.RadiusAt(profile.IndexAt(-30f)), precision: 3);
        Assert.Equal(expectedFinal, profile.RadiusAt(profile.IndexAt(-60f)), precision: 3);

        // Untouched beyond the turned length.
        Assert.Equal(26f, profile.RadiusAt(profile.IndexAt(-64f)), precision: 3);
    }

    [Fact]
    public void Sweep_ReportsSeveredIndex_WhenAPartingCutReachesCenterline()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -70f, zMax: 5f, stepMm: 0.5f);

        var result = profile.Sweep(new Vector2(25f, -55f), new Vector2(-0.5f, -55f), 0f, 0.1f, 600f);

        Assert.True(result.MaterialRemoved);
        Assert.Equal(profile.IndexAt(-55f), result.SeveredIndex);
    }

    [Fact]
    public void Sweep_DoesNotReportSeveredIndex_WhenCutDoesNotReachCenterline()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -70f, zMax: 5f, stepMm: 0.5f);

        var result = profile.Sweep(new Vector2(20f, -60f), new Vector2(20f, -30f), 0.8f, 0.15f, 800f);

        Assert.True(result.MaterialRemoved);
        Assert.Null(result.SeveredIndex);
    }

    [Fact]
    public void Sever_ZeroesBeyondTheGivenIndex_AndReturnsThePreSeverRadii()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -10f, zMax: 10f, stepMm: 1f);
        // Cut a step from Z0 to Z10 down to radius 15 so the severed piece
        // has a non-uniform shape worth asserting on.
        profile.Sweep(new Vector2(15f, 0f), new Vector2(15f, 10f), 0f, 0f, 0f);
        int severIndex = profile.IndexAt(-1f); // one bucket before the cut range, still at full radius

        var removed = profile.Sever(severIndex);

        // Returned radii run from just past severIndex to the end.
        Assert.Equal(profile.BucketCount - severIndex - 1, removed.Length);
        Assert.Equal(15f, removed[0], precision: 3); // bucket right after the sever point
        Assert.Equal(15f, removed[^1], precision: 3); // bucket at ZMax

        // Beyond the sever point, the live profile is now zeroed.
        Assert.Equal(0f, profile.RadiusAt(severIndex + 1), precision: 3);
        Assert.Equal(0f, profile.RadiusAt(profile.BucketCount - 1), precision: 3);
        // At and before the sever point, untouched.
        Assert.Equal(25f, profile.RadiusAt(severIndex), precision: 3);
        Assert.Equal(25f, profile.RadiusAt(profile.IndexAt(-10f)), precision: 3);
    }

    [Fact]
    public void Sever_ASecondTime_StopsAtThePriorSeverBoundary_NotTheArrayEnd()
    {
        // Two part-offs on the same bar: the second severed piece must only
        // contain the bucket range actually still live between the two cut
        // points, not re-include the first piece's already-zeroed tail.
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -30f, zMax: 10f, stepMm: 1f);

        int firstSeverIndex = profile.IndexAt(0f);
        profile.Sever(firstSeverIndex); // detaches Z1..Z10

        int secondSeverIndex = profile.IndexAt(-10f);
        var secondRemoved = profile.Sever(secondSeverIndex); // should only cover Z-9..Z0

        Assert.Equal(firstSeverIndex - secondSeverIndex, secondRemoved.Length);
        Assert.All(secondRemoved, r => Assert.Equal(25f, r, precision: 3));
    }

    [Fact]
    public void Sweep_BeyondTheEndOfTheBar_CutsNothing()
    {
        var profile = new LatheRadiusProfile(25f, -85f, 5f);
        var result = profile.Sweep(new Godot.Vector2(0f, 40f), new Godot.Vector2(100f, 40f), 0.8f, 0.1f, 800f);
        Assert.False(result.MaterialRemoved);
        Assert.Equal(25f, profile.RadiusAt(profile.BucketCount - 1));
    }
}
