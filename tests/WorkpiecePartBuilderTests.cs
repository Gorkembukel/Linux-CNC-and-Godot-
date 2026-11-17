using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

/// <summary>Only the pure-math half (ComputeVolumeAndCentroid) and the
/// degenerate-input guard are exercised here -- Build's mesh/collision
/// construction needs the Godot engine (SurfaceTool/ArrayMesh/
/// ConvexPolygonShape3D) and is verified live instead, same as
/// WorkpieceVisual's own mesh building always has been.</summary>
public class WorkpiecePartBuilderTests
{
    [Fact]
    public void ComputeVolumeAndCentroid_UniformCylinder_MatchesAnalyticFormula()
    {
        const float radius = 10f, stepMm = 1f, startZmm = 0f;
        const int n = 11; // 0..10mm, 10 segments
        var radii = new float[n];
        for (int i = 0; i < n; i++)
            radii[i] = radius;

        var (volumeMm3, centroidZmm) = WorkpiecePartBuilder.ComputeVolumeAndCentroid(radii, stepMm, startZmm);

        double length = (n - 1) * stepMm;
        double expectedVolume = System.Math.PI * radius * radius * length;
        Assert.Equal(expectedVolume, volumeMm3, precision: 3);
        Assert.Equal(startZmm + length / 2.0, centroidZmm, precision: 3);
    }

    [Fact]
    public void ComputeVolumeAndCentroid_LinearTaper_MatchesWholeFrustumFormula_RegardlessOfSampleCount()
    {
        // A true linear taper's volume, summed over any number of
        // sub-segments, must exactly equal the single whole-frustum closed
        // form (π·h/3·(r0^2+r0·r1+r1^2)) -- a real cone-geometry identity,
        // not an approximation, so this guards against a buggy
        // average-radius-cylinder-style approximation sneaking in instead.
        const float r0 = 12f, r1 = 4f, stepMm = 2f, startZmm = -20f;
        const int n = 6; // 5 segments, length 10mm
        var radii = new float[n];
        for (int i = 0; i < n; i++)
            radii[i] = Mathf.Lerp(r0, r1, i / (float)(n - 1));

        var (volumeMm3, _) = WorkpiecePartBuilder.ComputeVolumeAndCentroid(radii, stepMm, startZmm);

        double wholeLength = (n - 1) * stepMm;
        double expectedVolume = System.Math.PI * wholeLength / 3.0 * (r0 * r0 + r0 * r1 + r1 * r1);
        Assert.Equal(expectedVolume, volumeMm3, precision: 3);
    }

    [Fact]
    public void Build_ReturnsNull_WhenFewerThanTwoSamples()
    {
        // A real edge case this training sim allows (an operator parting off
        // right at the very tip), not a hypothetical -- must not spawn a
        // degenerate object. Both return before touching any Godot engine
        // mesh/shape object, so this is safe to run outside the engine.
        Assert.Null(WorkpiecePartBuilder.Build(new float[] { 5f }, 1f, 0f, 7850f));
        Assert.Null(WorkpiecePartBuilder.Build(System.Array.Empty<float>(), 1f, 0f, 7850f));
    }

    [Fact]
    public void Build_ReturnsNull_ForAFacingSliverShorterThanTheMinimumPartLength()
    {
        // Found live: the demo program's very first move ("face the end") is
        // a pure-X plunge at one Z bucket -- structurally identical to a
        // parting cut in this 1D profile model, so it was spawning a tiny
        // facing-waste stub as a "finished part" every run. 5 buckets at
        // 0.5mm = 2mm, under the 3mm minimum, so this must be skipped.
        var radii = new float[] { 26f, 26f, 26f, 26f, 26f };
        Assert.Null(WorkpiecePartBuilder.Build(radii, 0.5f, 0f, 7850f));
    }
}
