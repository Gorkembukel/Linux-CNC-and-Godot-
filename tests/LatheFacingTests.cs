using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class LatheFacingTests
{
    private static ToolProfile C80() => new(InsertOutline.Build(new InsertParams(InsertShape.C80, 12f, 0.4f, 95f, ToolHand.Right)));

    private static LatheRadiusProfile Bar() => new(25f, -60f, 0f);

    [Fact]
    public void PlungeAtTheCentre_OnlyOpensWhatTheInsertCovers()
    {
        var bar = Bar();
        var tool = C80();
        bar.SweepProfile(new Vector2(0f, 3f), new Vector2(0f, -1f), tool);

        int face = bar.IndexAt(-0.5f);
        Assert.Equal(25f, bar.RadiusAt(face), precision: 3);
        Assert.InRange(bar.InnerAt(face), 1f, tool.Height + 0.5f);
    }

    [Fact]
    public void FacingOutFromTheCentre_LeavesTheSameFaceAsFacingIn()
    {
        var outward = Bar();
        outward.SweepProfile(new Vector2(0f, 3f), new Vector2(0f, -1f), C80());
        outward.SweepProfile(new Vector2(0f, -1f), new Vector2(27f, -1f), C80());
        var inward = Bar();
        inward.SweepProfile(new Vector2(27f, -1f), new Vector2(-0.5f, -1f), C80());

        foreach (var bar in new[] { outward, inward })
        {
            Assert.Equal(0f, bar.RadiusAt(bar.IndexAt(-0.5f)), precision: 3);
            Assert.Equal(25f, bar.RadiusAt(bar.IndexAt(-1.5f)), precision: 3);
            Assert.Equal(0f, bar.InnerAt(bar.IndexAt(-1.5f)), precision: 3);
        }
    }

    [Fact]
    public void PartialFaceFromTheCentre_LeavesTheRimBeyondTheInsert()
    {
        var bar = Bar();
        var tool = C80();
        bar.SweepProfile(new Vector2(0f, 3f), new Vector2(0f, -1f), tool);
        bar.SweepProfile(new Vector2(0f, -1f), new Vector2(5f, -1f), tool);

        int face = bar.IndexAt(-0.5f);
        Assert.Equal(25f, bar.RadiusAt(face), precision: 3);
        Assert.InRange(bar.InnerAt(face), 5f, 5f + tool.Height + 0.5f);
    }

    [Fact]
    public void PlainTool_PlungeAtTheCentre_KeepsTheOutside()
    {
        var bar = Bar();
        bar.Sweep(new Vector2(0f, 3f), new Vector2(0f, -1f), 0.4f, 0.1f, 800f);

        int face = bar.IndexAt(-0.5f);
        Assert.Equal(25f, bar.RadiusAt(face), precision: 3);
        Assert.True(bar.InnerAt(face) > 0f);
    }
}
