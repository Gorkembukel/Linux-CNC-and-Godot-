using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class VoxelSawTests
{
    [Fact]
    public void RemoveBeyond_LeavesThisSide_ShortOfTheKerf()
    {
        var block = VoxelStock.Block(100f, 20f, 20f, 0.5f);
        block.RemoveBeyond(new Vector3(10f, 0f, 0f), Vector3.Right, 0.5f);

        var bounds = block.MaterialBounds().Value;
        Assert.InRange(bounds.Max.X, 9.0f, 10.0f);
        Assert.InRange(bounds.Min.X, -50.5f, -49.5f);
        Assert.InRange(block.VolumeMm3(), 59.5f * 400f * 0.97f, 59.5f * 400f * 1.03f);
    }

    [Fact]
    public void PlaneSection_StraightAndAt45Degrees_ThroughABar()
    {
        var bar = VoxelStock.CylinderAlongX(200f, 30f, 0.5f);
        var middle = new Vector3(20f, -15f, 0f);

        var straight = bar.PlaneSection(middle, Vector3.Right, Vector3.Down).Value;
        Assert.InRange(straight.AreaMm2, 707f * 0.95f, 707f * 1.05f);
        Assert.InRange(straight.WidthMm, 29f, 32f);
        Assert.InRange(straight.DepthMm, 29f, 32f);

        var tilted = bar.PlaneSection(middle, new Vector3(1f, 0f, 1f).Normalized(), Vector3.Down).Value;
        Assert.InRange(tilted.AreaMm2, 1000f * 0.94f, 1000f * 1.06f);
        Assert.InRange(tilted.WidthMm, 41f, 44f);
        Assert.InRange(tilted.DepthMm, 29f, 32f);
    }

    [Fact]
    public void Cropped_KeepsTheMaterial_OnASmallerGrid()
    {
        var bar = VoxelStock.CylinderAlongX(200f, 30f, 0.5f);
        bar.RemoveBeyond(new Vector3(-40f, 0f, 0f), Vector3.Right, 0.5f);
        var bounds = bar.MaterialBounds().Value;
        var cropped = bar.Cropped(bounds.Min, bounds.Max);

        Assert.True(cropped.CountX < bar.CountX);
        Assert.InRange(cropped.VolumeMm3(), bar.VolumeMm3() * 0.99f, bar.VolumeMm3() * 1.01f);
    }
}
