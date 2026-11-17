using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class LatheSectionTests
{
    private static readonly Vector2 Square25 = new(25f, 25f);

    [Fact]
    public void Area_IsTheBlockUntilTheToolReachesIt_AndTheCircleOnceInside()
    {
        Assert.Equal(2500f, LatheSection.Area(LatheSection.Corner(Square25), Square25), precision: 0);
        Assert.Equal(Mathf.Pi * 400f, LatheSection.Area(20f, Square25), precision: 1);
        float between = LatheSection.Area(30f, Square25);
        Assert.True(between > Mathf.Pi * 625f && between < 2500f);
    }

    [Fact]
    public void Rho_FollowsTheFlatsAndTheArc()
    {
        Assert.Equal(25f, LatheSection.Rho(40f, 0f, Square25), precision: 3);
        Assert.Equal(25f * Mathf.Sqrt(2f), LatheSection.Rho(40f, Mathf.Pi / 4f, Square25), precision: 3);
        Assert.Equal(30f, LatheSection.Rho(30f, Mathf.Pi / 4f, Square25), precision: 3);
    }

    [Fact]
    public void TurningASquareBlock_RemovesOnlyItsCornersFirst()
    {
        var radii = System.Linq.Enumerable.Repeat(LatheSection.Corner(Square25), 41).ToArray();
        var profile = new LatheRadiusProfile(radii, -20f, 0.5f, squareHalfMm: Square25);

        var corners = profile.Sweep(new Vector2(30f, 0f), new Vector2(30f, -20f), 0f, 0.1f, 500f);
        float cornerArea = 2500f - LatheSection.Area(30f, Square25);
        Assert.InRange(corners.VolumeMm3, cornerArea * 20.5f * 0.99f, cornerArea * 20.5f * 1.01f);
        Assert.True(corners.DepthOfCutMm <= 0f);

        var flats = profile.Sweep(new Vector2(24f, 0f), new Vector2(24f, -20f), 0f, 0.1f, 500f);
        Assert.Equal(1f, flats.DepthOfCutMm, precision: 3);
    }

    [Fact]
    public void RevolvedSquare_KeepsTheFlatsWhereTheProfileIsStillOutsideThem()
    {
        var radii = System.Linq.Enumerable.Repeat(30f, 41).ToArray();
        var stock = VoxelStock.Revolved(radii, new float[41], 0.5f, 0.5f, Square25);

        // Axis at y = -25; a point 24 mm out along a flat is inside, 26 mm is outside the flat.
        Assert.True(stock.SampleDistance(new Vector3(0f, -25f + 24f, 0f)) > 0f);
        Assert.True(stock.SampleDistance(new Vector3(0f, -25f, 24f)) > 0f);
        Assert.True(stock.SampleDistance(new Vector3(0f, -25f + 22f, 22f)) < 0f); // corner already turned off
    }
}
