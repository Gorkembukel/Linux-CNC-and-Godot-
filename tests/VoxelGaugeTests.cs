using CncTrainer.Interaction;
using CncTrainer.Material;
using Godot;

namespace CncTrainer.Tests;

public class VoxelGaugeTests
{
    /// <summary>A 100 x 30 x 60 block (top at y = 0) with a Ø10 hole drilled
    /// right through at x = -20 and a Ø10 hole 8 deep at x = +20.</summary>
    private static VoxelStock Drilled()
    {
        var stock = VoxelStock.Block(100f, 30f, 60f, 0.5f);
        var drill = new CutterShape(5f, 0f, 50f, 0f);
        stock.Cut(new Transform3D(Basis.Identity, new Vector3(-20f, 5f, 0f)), new Transform3D(Basis.Identity, new Vector3(-20f, -40f, 0f)), drill);
        stock.Cut(new Transform3D(Basis.Identity, new Vector3(20f, 5f, 0f)), new Transform3D(Basis.Identity, new Vector3(20f, -8f, 0f)), drill);
        return stock;
    }

    private static float Read(string reading, string axis)
    {
        int at = reading.IndexOf(axis + " ", System.StringComparison.Ordinal) + axis.Length + 1;
        int end = reading.IndexOf(" mm", at, System.StringComparison.Ordinal);
        return float.Parse(reading[at..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Caliper_ReadsAHoleDrilledRightThrough()
    {
        string reading = VoxelGauge.Read(Drilled(), HandToolKind.Caliper, new Vector3(-20f, 0f, 0f));
        Assert.Contains("iç ölçü", reading);
        Assert.InRange(Read(reading, "X"), 9.5f, 10.5f);
        Assert.InRange(Read(reading, "Y"), 9.5f, 10.5f);
    }

    [Fact]
    public void Caliper_ReadsABlindHole()
    {
        string reading = VoxelGauge.Read(Drilled(), HandToolKind.Caliper, new Vector3(20f, 0f, 0f));
        Assert.Contains("iç ölçü", reading);
        Assert.InRange(Read(reading, "X"), 9.5f, 10.5f);
    }

    [Fact]
    public void Caliper_OnTheTopFace_ReadsTheOutside()
    {
        string reading = VoxelGauge.Read(Drilled(), HandToolKind.Caliper, new Vector3(0f, 0f, 20f));
        Assert.Contains("dış ölçü", reading);
    }

    [Fact]
    public void OffThePart_NothingToMeasure_AndADepthGaugeFindsNoFloorInAThroughHole()
    {
        var stock = Drilled();
        Assert.Contains("malzeme yok", VoxelGauge.Read(stock, HandToolKind.Caliper, new Vector3(200f, 0f, 0f)));
        Assert.Contains("dip yok", VoxelGauge.Read(stock, HandToolKind.DepthGauge, new Vector3(-20f, 0f, 0f)));
    }
}
