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
    public void FinePitch_OnAFineProfile_ReachesItsFullDepth_AndRemembersThePitch()
    {
        var bar = new LatheRadiusProfile(10f, -40f, 0f, 0.1f);
        var thread = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.Thread60, 0f, 0.1f, 90f, ToolHand.Neutral, DepthMm: 4f)));
        float depth = 0.613f;
        for (float d = 0.2f; d < depth + 0.1f; d += 0.2f)
            bar.SweepProfile(new Vector2(10f - Mathf.Min(d, depth), 2f), new Vector2(10f - Mathf.Min(d, depth), -30f), thread, threadPitchMm: 1f, anchorZ: 2f);

        var section = Enumerable.Range(0, 10).Select(k => bar.RadiusAt(bar.IndexAt(-15f + k * 0.1f))).ToArray();
        Assert.InRange(section.Max() - section.Min(), depth - 0.05f, depth + 0.05f);
        Assert.Equal(1f, bar.PitchAt(bar.IndexAt(-15f)));
        Assert.Equal(0f, bar.PitchAt(bar.IndexAt(-35f)));
        Assert.Equal(1f, bar.MaxPitchMm);
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

    [Fact]
    public void GroovingBlade_PartingToTheAxis_SeversThePart()
    {
        var bar = new LatheRadiusProfile(18f, -185f, 0f, 0.5f);
        var blade = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.Groove, 0f, 0.2f, 90f, ToolHand.Neutral, WidthMm: 3f, DepthMm: 20f)));
        int? severed = null;
        var from = new Vector2(19f, -55f);
        for (float x = 19f; x >= -0.5f; x -= 0.25f)
        {
            var to = new Vector2(x, -55f);
            var r = bar.SweepProfile(from, to, blade);
            severed ??= r.SeveredIndex;
            from = to;
        }
        Assert.NotNull(severed);
        var piece = bar.Sever(severed.Value);
        Assert.InRange((piece.Length - 1) * 0.5f, 50f, 55f);
        Assert.All(piece, r => Assert.Equal(18f, r));
    }
}
