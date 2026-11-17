using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class LatheBoreTests
{
    [Fact]
    public void Drill_OpensAHoleOnlyAsDeepAsItWent()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -100f, zMax: 0f, stepMm: 0.5f);

        var result = profile.SweepBore(new Vector2(0f, 2f), new Vector2(0f, -30f), 0f, drillRadius: 6f, pointAngleDeg: 90f);

        Assert.True(result.MaterialRemoved);
        Assert.Equal(6f, profile.InnerAt(profile.IndexAt(-10f)), precision: 3);
        Assert.Equal(6f, profile.InnerAt(profile.IndexAt(-24f)), precision: 3);
        Assert.Equal(2f, profile.InnerAt(profile.IndexAt(-28f)), precision: 3);
        Assert.Equal(0f, profile.InnerAt(profile.IndexAt(-30f)), precision: 3);
        Assert.Equal(0f, profile.InnerAt(profile.IndexAt(-31f)), precision: 3);
        Assert.Equal(25f, profile.RadiusAt(profile.IndexAt(-10f)), precision: 3);
    }

    [Fact]
    public void Drill_FirstTouchOnlyOpensTheTipOfTheCone()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -100f, zMax: 0f, stepMm: 0.5f);

        profile.SweepBore(new Vector2(0f, 2f), new Vector2(0f, -1f), 0f, drillRadius: 8f, pointAngleDeg: 118f);

        float slope = Mathf.Tan(Mathf.DegToRad(59f));
        Assert.Equal(0.5f * slope, profile.InnerAt(profile.IndexAt(-0.5f)), precision: 3);
        Assert.Equal(1f * slope, profile.InnerAt(profile.IndexAt(0f)), precision: 3);
        Assert.Equal(0f, profile.InnerAt(profile.IndexAt(-1.5f)), precision: 3);
    }

    [Fact]
    public void Boring_WidensTheBore_ButNeverShrinksIt()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -100f, zMax: 0f, stepMm: 0.5f);
        profile.SweepBore(new Vector2(0f, 2f), new Vector2(0f, -30f), 0f, drillRadius: 6f);

        profile.SweepBore(new Vector2(10.4f, 1f), new Vector2(10.4f, -20f), 0.4f, drillRadius: 0f);
        Assert.Equal(10f, profile.InnerAt(profile.IndexAt(-15f)), precision: 3);
        Assert.Equal(6f, profile.InnerAt(profile.IndexAt(-25f)), precision: 3);

        var narrower = profile.SweepBore(new Vector2(8f, 1f), new Vector2(8f, -20f), 0.4f, drillRadius: 0f);
        Assert.False(narrower.MaterialRemoved);
        Assert.Equal(10f, profile.InnerAt(profile.IndexAt(-15f)), precision: 3);
    }

    [Fact]
    public void TurningDownToTheBore_DropsTheRing()
    {
        var profile = new LatheRadiusProfile(stockRadiusMm: 25f, zMin: -100f, zMax: 0f, stepMm: 0.5f);
        profile.SweepBore(new Vector2(0f, 2f), new Vector2(0f, -40f), 0f, drillRadius: 8f);

        var result = profile.Sweep(new Vector2(30f, -30f), new Vector2(8f, -30f), 0f, 0f, 500f);

        Assert.True(result.MaterialRemoved);
        Assert.Equal(profile.IndexAt(-30f), result.SeveredIndex);
        var removed = profile.Sever(result.SeveredIndex!.Value, out var inner);
        Assert.Equal(25f, removed[0], precision: 3);
        Assert.Equal(8f, inner[0], precision: 3);
        Assert.Equal(0f, profile.InnerAt(profile.IndexAt(-10f)), precision: 3);
    }

    [Fact]
    public void BoredPart_WeighsLessThanTheSolidOne()
    {
        var radii = new[] { 25f, 25f, 25f };
        var (solid, _) = WorkpiecePartBuilder.ComputeVolumeAndCentroid(radii, 10f, 0f);
        var (bored, _) = WorkpiecePartBuilder.ComputeVolumeAndCentroid(radii, 10f, 0f, new[] { 10f, 10f, 10f });
        Assert.Equal(solid * (625.0 - 100.0) / 625.0, bored, precision: 3);
    }
}
