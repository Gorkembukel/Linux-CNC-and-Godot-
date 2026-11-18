using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Shared triangle-building math for "revolve a radius-vs-X profile around
/// the X axis" -- extracted out of WorkpieceVisual.RebuildChunk/AddCap so
/// WorkpiecePartBuilder (finished, detached parts) can build the exact same
/// kind of mesh without duplicating this triangle math. Both call sites still
/// own their own MeshBuffer/loop; this only factors out the per-ring/per-cap
/// geometry. `clip` is a block's half sizes across the axis (LatheSection),
/// in the same unit as the radii; zero = round.
/// </summary>
public static class RevolutionMesh
{
    private static Vector3 At(float x, float r, float t, Vector2 clip)
    {
        float rho = LatheSection.Rho(r, t, clip);
        return new Vector3(x, rho * Mathf.Cos(t), rho * Mathf.Sin(t));
    }

    /// <summary>One ring-to-ring band of the revolved surface, between axial
    /// position/radius (x0,r0) and (x1,r1).</summary>
    public static void AddRingSegment(MeshBuffer st, float x0, float r0, float x1, float r1, int angularSegments, Vector2 clip = default)
    {
        for (int a = 0; a < angularSegments; a++)
        {
            float t0 = a * Mathf.Tau / angularSegments;
            float t1 = (a + 1) * Mathf.Tau / angularSegments;
            Vector3 p00 = At(x0, r0, t0, clip), p01 = At(x0, r0, t1, clip);
            Vector3 p10 = At(x1, r1, t0, clip), p11 = At(x1, r1, t1, clip);

            st.AddTriangle(p00, p10, p11);
            st.AddTriangle(p00, p11, p01);
        }
    }

    /// <summary>As AddRingSegment with each ring given per angle step (index
    /// a at angle a * tau / n): radius r[a], set back along the axis by
    /// back[a] (null = 0). A thread's helix; endFace: the band closing it to
    /// the bore at the bar's end.</summary>
    public static void AddRingSegment(MeshBuffer st, float x0, float[] r0, float[] back0, float x1, float[] r1, float[] back1, bool endFace = false)
    {
        int n = r0.Length;
        st.SetSmoothGroup(endFace ? ThreadEndGroup : OutsideGroup);
        for (int a = 0; a < n; a++)
        {
            int b = (a + 1) % n;
            float t0 = a * Mathf.Tau / n, t1 = (a + 1) * Mathf.Tau / n;
            Vector3 p00 = At(x0 - (back0?[a] ?? 0f), r0[a], t0, default), p01 = At(x0 - (back0?[b] ?? 0f), r0[b], t1, default);
            Vector3 p10 = At(x1 - (back1?[a] ?? 0f), r1[a], t0, default), p11 = At(x1 - (back1?[b] ?? 0f), r1[b], t1, default);
            st.AddTriangle(p00, p10, p11);
            st.AddTriangle(p00, p11, p01);
        }
    }

    /// <summary>A band of a bore: like AddRingSegment, facing the axis.</summary>
    public static void AddBoreSegment(MeshBuffer st, float x0, float r0, float x1, float r1, int angularSegments) =>
        AddRingSegment(st, x1, r1, x0, r0, angularSegments);

    // Normals are smoothed across every vertex shared within a group:
    // flat faces (ends, shoulders, a hole's bottom, a block's sides) get no
    // smoothing, so their edges stay sharp instead of blending into the
    // surfaces next to them.
    private const uint Flat = MeshBuffer.Flat, OutsideGroup = 0, BoreGroup = 1, ThreadEndGroup = 2;

    private static uint GroupFor(float x0, float r0, float x1, float r1, uint round) =>
        Mathf.Abs(r1 - r0) > 4f * Mathf.Abs(x1 - x0) ? Flat : round;

    /// <summary>A flat end cap at axial position x, radius r; a ring when
    /// there is a bore of radius innerR.</summary>
    public static void AddCap(MeshBuffer st, float x, float r, int angularSegments, bool outwardNormalIsNegativeX, float innerR = 0f, Vector2 clip = default)
    {
        st.SetSmoothGroup(Flat);
        for (int a = 0; a < angularSegments; a++)
        {
            float t0 = a * Mathf.Tau / angularSegments;
            float t1 = (a + 1) * Mathf.Tau / angularSegments;
            Vector3 p0 = At(x, r, t0, clip), p1 = At(x, r, t1, clip);
            Vector3 q0 = new(x, innerR * Mathf.Cos(t0), innerR * Mathf.Sin(t0));
            Vector3 q1 = new(x, innerR * Mathf.Cos(t1), innerR * Mathf.Sin(t1));
            if (outwardNormalIsNegativeX)
            {
                st.AddTriangle(q0, p0, p1);
                if (innerR > 0f)
                    st.AddTriangle(q0, p1, q1);
            }
            else
            {
                st.AddTriangle(q0, p1, p0);
                if (innerR > 0f)
                    st.AddTriangle(q0, q1, p1);
            }
        }
    }

    /// <summary>The surfaces between two neighbouring profile samples: the
    /// outside, the bore, and where the bar ends after a bore, the end ring.</summary>
    public static void AddSection(MeshBuffer st, float x0, float r0, float in0, float x1, float r1, float in1, int angularSegments, Vector2 clip = default)
    {
        // Callers pass metres or millimetres; a bucket that was cut away is exactly 0.
        const float gone = 0f;
        if (r1 <= gone && in0 > 0f)
        {
            st.SetSmoothGroup(Flat);
            AddRingSegment(st, x0, r0, x1, in0, angularSegments, clip);
            return;
        }
        if (r0 <= gone && in1 > 0f)
        {
            st.SetSmoothGroup(Flat);
            AddRingSegment(st, x0, in1, x1, r1, angularSegments, clip);
            return;
        }
        bool flats = clip != Vector2.Zero && Mathf.Max(r0, r1) > Mathf.Min(clip.X, clip.Y);
        st.SetSmoothGroup(flats ? Flat : GroupFor(x0, r0, x1, r1, OutsideGroup));
        AddRingSegment(st, x0, r0, x1, r1, angularSegments, clip);
        if (in0 > 0f || in1 > 0f)
        {
            st.SetSmoothGroup(GroupFor(x0, in0, x1, in1, BoreGroup));
            AddBoreSegment(st, x0, in0, x1, in1, angularSegments);
        }
    }
}
