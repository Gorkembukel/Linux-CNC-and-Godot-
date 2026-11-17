using Godot;
using CncTrainer.Material;

namespace Tests;

public class MillHeightmapTests
{
    private static MillHeightmap NewStock() => new(sizeXMm: 40f, sizeYMm: 20f, thicknessMm: 10f, stepMm: 1f);

    [Fact]
    public void Plunge_CutsADiscOfToolRadius()
    {
        var hm = NewStock();
        var r = hm.Sweep(new Vector3(0, 0, 5), new Vector3(0, 0, -3), toolRadiusMm: 3f);

        Assert.True(r.MaterialRemoved);
        Assert.Equal(3f, r.MaxDepthMm, 3);
        Assert.Equal(-3f, hm.HeightAt(20, 10), 3);
        Assert.Equal(-3f, hm.HeightAt(23, 10), 3);
        Assert.Equal(0f, hm.HeightAt(24, 10), 3);
        Assert.Equal(0f, hm.HeightAt(23, 13), 3);
    }

    [Fact]
    public void Slot_CutsFullWidthAlongTheWholeMove()
    {
        var hm = NewStock();
        hm.Sweep(new Vector3(-10, 0, -2), new Vector3(10, 0, -2), toolRadiusMm: 2f);

        for (int ix = 10; ix <= 30; ix++)
        {
            Assert.Equal(-2f, hm.HeightAt(ix, 10), 3);
            Assert.Equal(-2f, hm.HeightAt(ix, 12), 3);
            Assert.Equal(0f, hm.HeightAt(ix, 13), 3);
        }
        Assert.Equal(-2f, hm.HeightAt(32, 10), 3);
        Assert.Equal(0f, hm.HeightAt(33, 10), 3);
    }

    [Fact]
    public void MoveAboveTheSurface_RemovesNothing()
    {
        var hm = NewStock();
        var r = hm.Sweep(new Vector3(-10, 0, 1), new Vector3(10, 0, 0), toolRadiusMm: 2f);
        Assert.False(r.MaterialRemoved);
    }

    [Fact]
    public void Ramp_IsDeepestWhereTheToolEndsUp()
    {
        var hm = NewStock();
        hm.Sweep(new Vector3(-10, 0, 0), new Vector3(10, 0, -4), toolRadiusMm: 1f);

        Assert.True(hm.HeightAt(15, 10) > hm.HeightAt(25, 10));
        Assert.Equal(-4f, hm.HeightAt(30, 10), 3);
        // The cutter's trailing edge passes each node last, at its deepest.
        Assert.Equal(-2.2f, hm.HeightAt(20, 10), 3);
    }

    [Fact]
    public void RepeatingTheSameCut_RemovesNothingTheSecondTime()
    {
        var hm = NewStock();
        var first = hm.Sweep(new Vector3(-5, 0, -1), new Vector3(5, 0, -1), toolRadiusMm: 2f);
        var second = hm.Sweep(new Vector3(-5, 0, -1), new Vector3(5, 0, -1), toolRadiusMm: 2f);

        Assert.True(first.VolumeMm3 > 0f);
        Assert.False(second.MaterialRemoved);
    }

    [Fact]
    public void Cut_NeverGoesBelowTheStockBottom()
    {
        var hm = NewStock();
        hm.Sweep(new Vector3(0, 0, 5), new Vector3(0, 0, -50), toolRadiusMm: 2f);
        Assert.Equal(-10f, hm.HeightAt(20, 10), 3);
    }

    [Fact]
    public void DirtyRange_CoversExactlyTheNodesThatChanged()
    {
        var hm = NewStock();
        var r = hm.Sweep(new Vector3(0, 0, 1), new Vector3(0, 0, -1), toolRadiusMm: 2f);
        Assert.Equal((18, 22, 8, 12), (r.MinIx, r.MaxIx, r.MinIy, r.MaxIy));
    }
}
