using System.Linq;
using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class ToolProfileTests
{
    private static ToolProfile Insert(InsertShape shape, float approach, float nose = 0.4f, float edge = 12f) =>
        new(InsertOutline.Build(new InsertParams(shape, edge, nose, approach, ToolHand.Right)));

    private static LatheRadiusProfile Bar() => new(25f, -60f, 5f);

    [Fact]
    public void Outline_HasTheControlledPointAtTheBoundingCorner()
    {
        var c80 = Insert(InsertShape.C80, 95f);
        Assert.Equal(0f, c80.Outline.Min(p => p.X), precision: 4);
        Assert.Equal(0f, c80.Outline.Min(p => p.Y), precision: 4);
        Assert.Equal(0f, c80.MinX(0.4f), precision: 1);
        Assert.True(float.IsNaN(c80.MinX(-1f)));
    }

    [Fact]
    public void Descending_V35FollowsTheSlope_C80GougesItWithItsTrailingEdge()
    {
        float GougeAt(InsertShape shape, float approach)
        {
            var bar = Bar();
            var tool = Insert(shape, approach);
            bar.SweepProfile(new Vector2(20f, 0f), new Vector2(10f, -20f), tool);
            return bar.RadiusAt(bar.IndexAt(-10f));
        }

        Assert.InRange(GougeAt(InsertShape.V35, 107.5f), 14.5f, 15.6f);
        Assert.True(GougeAt(InsertShape.C80, 95f) < 13f);
    }

    [Fact]
    public void GroovingBlade_CutsItsOwnWidth()
    {
        var bar = Bar();
        var blade = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.Groove, 0f, 0.2f, 90f, ToolHand.Neutral, WidthMm: 3f)));
        bar.SweepProfile(new Vector2(30f, -20f), new Vector2(18f, -20f), blade);

        Assert.InRange(bar.RadiusAt(bar.IndexAt(-18.5f)), 17.9f, 18.5f);
        Assert.Equal(25f, bar.RadiusAt(bar.IndexAt(-21f)), precision: 3);
        Assert.Equal(25f, bar.RadiusAt(bar.IndexAt(-16f)), precision: 3);
    }

    [Fact]
    public void Threading_LeavesAFormThatRepeatsEveryPitch()
    {
        var bar = Bar();
        var thread = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.Thread60, 0f, 0.1f, 90f, ToolHand.Neutral, DepthMm: 4f)));
        bar.SweepProfile(new Vector2(23f, 0f), new Vector2(23f, -40f), thread, threadPitchMm: 2f, anchorZ: 0f);

        for (float z = -30f; z < -28f; z += 0.5f)
            Assert.Equal(bar.RadiusAt(bar.IndexAt(z)), bar.RadiusAt(bar.IndexAt(z - 2f)), precision: 2);
        var section = Enumerable.Range(0, 5).Select(k => bar.RadiusAt(bar.IndexAt(-30f + k * 0.5f))).ToArray();
        Assert.True(section.Max() - section.Min() > 1f, "the thread has crests and roots, not a flat groove");
    }

    [Fact]
    public void BoringInsert_OpensTheBoreWithItsTopSide()
    {
        var bar = Bar();
        bar.SweepBore(new Vector2(0f, 5f), new Vector2(0f, -40f), 0f, 8f);
        var boring = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.C80, 6f, 0.4f, 95f, ToolHand.Right)), internalTool: true);
        bar.SweepProfileBore(new Vector2(11f, 2f), new Vector2(11f, -20f), boring);

        Assert.InRange(bar.InnerAt(bar.IndexAt(-15f)), 10.9f, 11.1f);
        Assert.Equal(8f, bar.InnerAt(bar.IndexAt(-35f)), precision: 3);
    }
}
