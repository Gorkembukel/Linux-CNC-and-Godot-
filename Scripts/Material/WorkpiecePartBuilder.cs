using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Turns a just-severed sub-range of a LatheRadiusProfile (see
/// LatheRadiusProfile.Sever) into a real, weighted, collidable object: the
/// finished part a parting cut just detached from the stock still held in
/// the chuck.
///
/// Split the same way LatheRadiusProfile (pure math) vs. WorkpieceVisual
/// (engine-dependent mesh building) already are in this codebase:
/// ComputeVolumeAndCentroid is plain floats/doubles, unit-testable without
/// the engine; Build additionally constructs Godot mesh/shape resources and
/// is only verified live, same as WorkpieceVisual's own mesh building always
/// has been.
///
/// Simplification, stated plainly rather than left for someone to discover:
/// the collision shape is a convex hull of the revolved profile's ring
/// points. That's correct for the stepped/tapered shapes ordinary turning
/// produces, but would smooth over a re-entrant groove if one is ever cut --
/// same spirit as LatheRadiusProfile's own nose-radius simplification and
/// ChipSpawner's visual-only chip physics. Not solved here; training-sim
/// scope, not CAM-grade.
/// </summary>
public static class WorkpiecePartBuilder
{
    public readonly struct BuiltPart
    {
        public readonly ArrayMesh Mesh;
        public readonly Shape3D Collision;
        public readonly float MassKg;
        public readonly float CentroidZmm;

        public BuiltPart(ArrayMesh mesh, Shape3D collision, float massKg, float centroidZmm)
        {
            Mesh = mesh;
            Collision = collision;
            MassKg = massKg;
            CentroidZmm = centroidZmm;
        }
    }

    /// <summary>Frustum-of-cone volume/centroid along the profile -- pure
    /// math, no Godot engine objects. `radiiMm[0]` is at `startZmm`, each
    /// following sample is `stepMm` further along Z.</summary>
    public static (double VolumeMm3, float CentroidZmm) ComputeVolumeAndCentroid(float[] radiiMm, float stepMm, float startZmm, float[] innerMm = null,
        Vector2 squareHalfMm = default)
    {
        double totalVolume = 0, zMoment = 0;
        for (int i = 0; i < radiiMm.Length - 1; i++)
        {
            float r0 = radiiMm[i], r1 = radiiMm[i + 1];
            float b0 = innerMm?[i] ?? 0f, b1 = innerMm?[i + 1] ?? 0f;
            double z0 = startZmm + i * stepMm, z1 = startZmm + (i + 1) * stepMm;
            double segLen = System.Math.Abs(z1 - z0);
            double segVolume = squareHalfMm == Vector2.Zero
                ? System.Math.PI * segLen / 3.0 * (r0 * r0 + r0 * r1 + r1 * r1 - (b0 * b0 + b0 * b1 + b1 * b1)) // mm^3
                : segLen * ((LatheSection.Area(r0, squareHalfMm) + LatheSection.Area(r1, squareHalfMm)) / 2.0 - System.Math.PI * (b0 * b0 + b1 * b1) / 2.0);
            totalVolume += segVolume;
            zMoment += segVolume * (z0 + z1) / 2.0;
        }
        float centroidZmm = totalVolume > 1e-9 ? (float)(zMoment / totalVolume) : startZmm;
        return (totalVolume, centroidZmm);
    }

    /// <summary>Builds the detached piece's mesh/collision/mass. `radiiMm` is
    /// the pre-sever radii LatheRadiusProfile.Sever returned (in order from
    /// the cut point toward the free/tip end); `startZmm` is the machine-mm Z
    /// of `radiiMm[0]`. Returns null if there's nothing to build (fewer than
    /// 2 samples -- e.g. an operator parting off right at the very tip,
    /// a real mistake this training sim allows, not a hypothetical).</summary>
    // Below this axial length, treat a "severance" as facing/parting waste
    // (a thin sliver/disc), not a real finished part worth spawning -- found
    // live: the demo program's very first move ("face the end") is a pure-X
    // plunge at one Z bucket structurally identical to a parting cut in this
    // profile model, so it was spawning a tiny stub every time. That stub
    // also happened to be thinner than Jolt's default convex margin
    // (0.04m), which failed to build at all ("initial triangle area too
    // small") -- this guard and the explicit Margin below both come from
    // that one live failure.
    private const float MinPartLengthMm = 3f;

    public static BuiltPart? Build(float[] radiiMm, float stepMm, float startZmm, float densityKgPerM3, int angularSegments = 24, float[] innerMm = null,
        Vector2 squareHalfMm = default)
    {
        if (radiiMm.Length < 2 || (radiiMm.Length - 1) * stepMm < MinPartLengthMm)
            return null;
        innerMm ??= new float[radiiMm.Length];

        var (volumeMm3, centroidZmm) = ComputeVolumeAndCentroid(radiiMm, stepMm, startZmm, innerMm, squareHalfMm);
        var clip = squareHalfMm * WorldScale.MmToM;
        if (clip != Vector2.Zero)
            angularSegments = 64;
        float massKg = (float)(volumeMm3 * 1e-9 * densityKgPerM3); // mm^3 -> m^3

        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        var hullPoints = new System.Collections.Generic.List<Vector3>();

        float LocalX(int index) => (float)(startZmm + index * stepMm - centroidZmm) * WorldScale.MmToM;

        for (int i = 0; i < radiiMm.Length - 1; i++)
        {
            float x0 = LocalX(i), x1 = LocalX(i + 1);
            float r0 = radiiMm[i] * WorldScale.MmToM, r1 = radiiMm[i + 1] * WorldScale.MmToM;
            RevolutionMesh.AddSection(st, x0, r0, innerMm[i] * WorldScale.MmToM, x1, r1, innerMm[i + 1] * WorldScale.MmToM, angularSegments, clip);
            AddRingPoints(hullPoints, x0, r0, angularSegments, clip);
        }

        int lastIndex = radiiMm.Length - 1;
        float xLast = LocalX(lastIndex), rLast = radiiMm[lastIndex] * WorldScale.MmToM;
        AddRingPoints(hullPoints, xLast, rLast, angularSegments, clip);

        RevolutionMesh.AddCap(st, LocalX(0), radiiMm[0] * WorldScale.MmToM, angularSegments, outwardNormalIsNegativeX: true, innerMm[0] * WorldScale.MmToM, clip);
        RevolutionMesh.AddCap(st, xLast, rLast, angularSegments, outwardNormalIsNegativeX: false, innerMm[lastIndex] * WorldScale.MmToM, clip);

        st.GenerateNormals();
        var mesh = st.Commit();
        // Explicit small margin: Jolt's default (0.04m) is bigger than many
        // realistic turned-part cross-sections, which fails hull building
        // outright rather than just being visually loose (found live).
        var collision = new ConvexPolygonShape3D { Points = hullPoints.ToArray(), Margin = 0.001f };

        return new BuiltPart(mesh, collision, massKg, centroidZmm);
    }

    private static void AddRingPoints(System.Collections.Generic.List<Vector3> points, float x, float r, int angularSegments, Vector2 clip)
    {
        for (int a = 0; a < angularSegments; a++)
        {
            float t = a * Mathf.Tau / angularSegments;
            float rho = LatheSection.Rho(r, t, clip);
            points.Add(new Vector3(x, rho * Mathf.Cos(t), rho * Mathf.Sin(t)));
        }
    }
}
