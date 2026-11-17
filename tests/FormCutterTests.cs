using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class FormCutterTests
{
    // A dovetail cutter: 12 mm wide at the tip, narrowing to a 6 mm neck 5 mm up.
    private static readonly Vector2[] Dovetail = { new(0f, 0f), new(6f, 0f), new(3f, 5f), new(3f, 30f) };

    [Fact]
    public void Distance_FollowsTheProfile()
    {
        var cutter = CutterShape.Form(Dovetail, 40f);
        Assert.Equal(6f, cutter.RadiusMm, precision: 3);
        Assert.True(cutter.Distance(5f, 1f) < 0f);
        Assert.True(cutter.Distance(5f, 6f) > 0f);
        Assert.True(cutter.Distance(2f, 20f) < 0f);
        Assert.True(cutter.Distance(0f, -1f) > 0f);
    }

    [Fact]
    public void ADovetailCut_IsWiderAtTheBottomThanAtTheTop()
    {
        var stock = VoxelStock.Block(60f, 20f, 40f, 0.5f);
        var cutter = CutterShape.Form(Dovetail, 40f);
        var from = new Transform3D(Basis.Identity, new Vector3(-40f, -8f, 0f));
        var to = new Transform3D(Basis.Identity, new Vector3(40f, -8f, 0f));
        var result = stock.Cut(from, to, cutter);

        Assert.True(result.MaterialRemoved);
        Assert.True(stock.SampleDistance(new Vector3(0f, -7.5f, 5f)) < 0f, "the wide bottom of the slot");
        Assert.True(stock.SampleDistance(new Vector3(0f, -2f, 5f)) > 0f, "the overhang above it stays");
    }
}
