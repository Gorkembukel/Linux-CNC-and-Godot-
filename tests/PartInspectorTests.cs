using Godot;
using CncTrainer.Inspection;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class PartInspectorTests
{
    private static FeatureSpec Feature(FeatureKind kind, float nominal, float lower, float upper, Vector3 position = default,
        Vector3 reference = default, Vector3? axis = null) =>
        new(kind.ToString(), kind, position, reference, axis ?? Vector3.Right, nominal, lower, upper);

    /// <summary>Ø20 for the first 10 mm, then Ø30 to 30 mm, a Ø8 bore in the first 5 mm.</summary>
    private static TurnedGeometry SteppedShaft()
    {
        const float step = 0.1f;
        var radii = new float[301];
        var inner = new float[301];
        for (int i = 0; i < radii.Length; i++)
        {
            radii[i] = i * step < 10f ? 10f : 15f;
            inner[i] = i * step < 5f ? 4f : 0f;
        }
        return new TurnedGeometry(radii, inner, step);
    }

    [Fact]
    public void TurnedPart_MeasuresDiametersBoreAndLength()
    {
        var results = PartInspector.Inspect(new[]
        {
            Feature(FeatureKind.OutsideDiameter, 20f, -0.05f, 0.05f, new Vector3(5f, 0f, 0f)),
            Feature(FeatureKind.OutsideDiameter, 30f, -0.05f, 0.05f, new Vector3(20f, 0f, 0f)),
            Feature(FeatureKind.BoreDiameter, 8f, 0f, 0.1f, new Vector3(2f, 0f, 0f)),
            Feature(FeatureKind.OverallLength, 30f, -0.2f, 0.2f),
        }, SteppedShaft());

        Assert.All(results, r => Assert.True(r.Pass, $"{r.Spec.Label} measured {r.MeasuredMm}"));
    }

    [Fact]
    public void TurnedPart_LyingTheOtherWayRound_IsReadFromTheMatchingEnd()
    {
        // The drawing's datum is the Ø30 end here.
        var results = PartInspector.Inspect(new[]
        {
            Feature(FeatureKind.OutsideDiameter, 30f, -0.05f, 0.05f, new Vector3(5f, 0f, 0f)),
            Feature(FeatureKind.OutsideDiameter, 20f, -0.05f, 0.05f, new Vector3(25f, 0f, 0f)),
        }, SteppedShaft());

        Assert.All(results, r => Assert.True(r.Pass));
    }

    [Fact]
    public void OutOfTolerance_AndMissingFeatures_Fail()
    {
        var results = PartInspector.Inspect(new[]
        {
            Feature(FeatureKind.OutsideDiameter, 19.9f, -0.02f, 0.02f, new Vector3(5f, 0f, 0f)),
            Feature(FeatureKind.Depth, 3f, -0.1f, 0.1f),
        }, SteppedShaft());

        Assert.False(results[0].Pass);
        Assert.Null(results[1].MeasuredMm);
        Assert.False(results[1].Pass);
    }

    [Fact]
    public void MilledPart_MeasuresSlotDepthWidthAndThickness()
    {
        var stock = VoxelStock.Block(100f, 30f, 60f, 0.5f);
        var cutter = new CutterShape(5f, 0f, 30f, 0f);
        stock.Cut(new Transform3D(Basis.Identity, new Vector3(-20f, -3f, 0f)), new Transform3D(Basis.Identity, new Vector3(20f, -3f, 0f)), cutter);

        var results = PartInspector.Inspect(new[]
        {
            Feature(FeatureKind.Depth, 3f, -0.05f, 0.05f, new Vector3(0f, 0f, 0f), new Vector3(-40f, 0f, 20f)),
            Feature(FeatureKind.Span, 10f, -0.1f, 0.1f, new Vector3(0f, -1.5f, 0f), axis: Vector3.Back),
            Feature(FeatureKind.Span, 30f, -0.05f, 0.05f, new Vector3(-40f, -15f, 20f), axis: Vector3.Up),
        }, new VoxelGeometry(stock));

        Assert.All(results, r => Assert.True(r.Pass, $"{r.Spec.Label} measured {r.MeasuredMm}"));
    }

    [Fact]
    public void Report_FailsOnMaterialOrIncidents_OnlyWhenTheOrderSaysSo()
    {
        var good = PartInspector.Inspect(new[] { Feature(FeatureKind.OverallLength, 30f, -0.2f, 0.2f) }, SteppedShaft());

        Assert.True(new InspectionReport("p", good, true, "", System.Array.Empty<string>(), true).Pass);
        Assert.False(new InspectionReport("p", good, false, "Çelik", System.Array.Empty<string>(), true).Pass);
        Assert.False(new InspectionReport("p", good, true, "", new[] { "crash" }, true).Pass);
        var lenient = new InspectionReport("p", good, true, "", new[] { "crash" }, false);
        Assert.True(lenient.Pass);
        Assert.Equal(85, lenient.Score);
        Assert.Contains("KABUL", lenient.Text());
    }

    [Fact]
    public void Roughness_ComesFromTheFinish_AndSurvivesTurningThePartOver()
    {
        var results = PartInspector.Inspect(new[]
        {
            Feature(FeatureKind.OutsideDiameter, 30f, -0.05f, 0.05f, new Vector3(5f, 0f, 0f)),
            Feature(FeatureKind.Roughness, 0f, 0f, 1.6f),
        }, new WithSurface(SteppedShaft(), 0.8f));

        Assert.All(results, r => Assert.True(r.Pass));
        Assert.Equal(0.8f, results[1].MeasuredMm);
    }

    [Fact]
    public void Ra_FollowsFeedSquaredOverNoseRadius_AndChatterIsRough()
    {
        // 0.2 mm/rev with a 0.8 nose radius: 0.04 / 25.6 mm = 1.56 um.
        Assert.Equal(1.5625f, CncTrainer.Machining.SurfaceFinish.RaUmFor(0.2f, 0.8f, 1, lathe: true), 0.001f);
        Assert.Equal(CncTrainer.Machining.SurfaceFinish.RaUmFor(0.1f, 5f, 1, lathe: false),
            CncTrainer.Machining.SurfaceFinish.RaUmFor(0.2f, 5f, 2, lathe: false), 5);
        var chatter = new CncTrainer.Machining.SurfaceFinish(false, 0.4f).WithChatter();
        Assert.True(chatter.Chatter);
        Assert.Equal(CncTrainer.Machining.SurfaceFinish.ChatterRaUm, chatter.RaUm);
    }

    [Fact]
    public void SizesOffOnAPartThatCrept_PointAtTheClamping()
    {
        var bad = PartInspector.Inspect(new[] { Feature(FeatureKind.OverallLength, 29f, -0.1f, 0.1f) }, SteppedShaft());
        var crept = new InspectionReport("p", bad, true, "", System.Array.Empty<string>(), true, SlipMm: 0.3f);
        var held = new InspectionReport("p", bad, true, "", System.Array.Empty<string>(), true, SlipMm: 0.005f);

        Assert.False(crept.Pass);
        Assert.Contains("0.30 mm", crept.Text());
        Assert.DoesNotContain("0.01 mm", held.Text());
        Assert.Contains("aşınma", held.Text());
    }
}
