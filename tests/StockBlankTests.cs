using Godot;
using CncTrainer.Chips;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Tests;

public class StockBlankTests
{
    [Fact]
    public void Block_MassComesFromVolumeAndDensity()
    {
        var blank = new StockBlank(StockShape.Block, new Vector3(120f, 80f, 30f), MaterialKind.Aluminum);
        Assert.Equal(288000f, blank.VolumeMm3, 0);
        Assert.Equal(0.7776f, blank.MassKg, 4);
        Assert.True(blank.BoundsM.DistanceTo(new Vector3(0.12f, 0.03f, 0.08f)) < 1e-6f);
    }

    [Fact]
    public void RoundBar_UsesLengthAndDiameter()
    {
        var blank = new StockBlank(StockShape.CylinderAlongX, new Vector3(100f, 50f, 50f), MaterialKind.Steel);
        Assert.Equal(Mathf.Pi * 625f * 100f, blank.VolumeMm3, 0);
        Assert.Equal(1.541f, blank.MassKg, 3);
        Assert.True(blank.BoundsM.DistanceTo(new Vector3(0.1f, 0.05f, 0.05f)) < 1e-6f);
    }
}
