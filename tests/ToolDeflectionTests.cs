using CncTrainer.Inspection;
using CncTrainer.Machining;
using CncTrainer.Spec;

namespace Tests;

public class ToolDeflectionTests
{
    private static ToolLimits EndMill(float stickOutMm) => new(false, false, 10f, stickOutMm, 2, ToolKind.EndMill);
    private static ToolLimits BoringBar(float overhangMm) => new(true, false, 16f, overhangMm, 1, ToolKind.Boring);

    [Fact]
    public void ShortCutter_LightCut_StaysWithinAFewMicrons()
    {
        Assert.InRange(EndMill(25f).DeflectionMm(150f), 0f, 0.005f);
    }

    [Fact]
    public void LongCutter_HeavyCut_SpringsOffBySizeMissingAmounts()
    {
        Assert.True(EndMill(70f).DeflectionMm(400f) > 0.05f);
    }

    [Fact]
    public void Deflection_GrowsWithTheCubeOfTheStickOut()
    {
        float ratio = EndMill(60f).DeflectionMm(300f) / EndMill(30f).DeflectionMm(300f);
        Assert.InRange(ratio, 7.99f, 8.01f);
    }

    [Fact]
    public void BoringBar_FarOut_GivesMoreThanItsShortSelf_AndMoreThanATurningTool()
    {
        var turning = new ToolLimits(true, false, 0f, 50f, 1, ToolKind.Turning);
        Assert.True(BoringBar(80f).DeflectionMm(500f) > 0.03f);
        Assert.True(BoringBar(30f).DeflectionMm(500f) < BoringBar(80f).DeflectionMm(500f) / 10f);
        Assert.True(turning.DeflectionMm(500f) < 0.002f);
    }

    [Fact]
    public void Drill_AndNoCut_DoNotSpring()
    {
        Assert.Equal(0f, new ToolLimits(false, false, 8f, 80f, 2, ToolKind.Drill).DeflectionMm(800f));
        Assert.Equal(0f, EndMill(70f).DeflectionMm(0f));
    }

    [Fact]
    public void Chatter_KeepsTheSpring()
    {
        Assert.Equal(0.04f, new SurfaceFinish(false, 0.8f, 0.04f).WithChatter().SpringMm);
    }

    [Fact]
    public void SizeMissedByASpringingTool_PointsAtStickOutAndASpringPass()
    {
        var spec = new FeatureSpec("Ø20", FeatureKind.OutsideDiameter, default, default, default, 20f, -0.02f, 0.02f);
        var bad = new[] { new FeatureResult(spec, 20.06f) };
        var sprang = new InspectionReport("p", bad, true, "", System.Array.Empty<string>(), true, SpringMm: 0.03f);
        var stiff = new InspectionReport("p", bad, true, "", System.Array.Empty<string>(), true, SpringMm: 0.002f);

        Assert.Contains("esnedi", sprang.Text());
        Assert.DoesNotContain("esnedi", stiff.Text());
    }
}
