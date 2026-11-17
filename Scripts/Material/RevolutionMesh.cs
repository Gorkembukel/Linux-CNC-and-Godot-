using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Shared triangle-building math for "revolve a radius-vs-X profile around
/// the X axis" -- extracted out of WorkpieceVisual.RebuildChunk/AddCap so
/// WorkpiecePartBuilder (finished, detached parts) can build the exact same
/// kind of mesh without duplicating this triangle math. Both call sites still
/// own their own SurfaceTool/loop; this only factors out the per-ring/per-cap
/// geometry.
/// </summary>
public static class RevolutionMesh
{
    public static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    /// <summary>One ring-to-ring band of the revolved surface, between axial
    /// position/radius (x0,r0) and (x1,r1).</summary>
    public static void AddRingSegment(SurfaceTool st, float x0, float r0, float x1, float r1, int angularSegments)
    {
        for (int a = 0; a < angularSegments; a++)
        {
            float t0 = a * Mathf.Tau / angularSegments;
            float t1 = (a + 1) * Mathf.Tau / angularSegments;
            Vector3 p00 = new(x0, r0 * Mathf.Cos(t0), r0 * Mathf.Sin(t0));
            Vector3 p01 = new(x0, r0 * Mathf.Cos(t1), r0 * Mathf.Sin(t1));
            Vector3 p10 = new(x1, r1 * Mathf.Cos(t0), r1 * Mathf.Sin(t0));
            Vector3 p11 = new(x1, r1 * Mathf.Cos(t1), r1 * Mathf.Sin(t1));

            AddTriangle(st, p00, p10, p11);
            AddTriangle(st, p00, p11, p01);
        }
    }

    /// <summary>A band of a bore: like AddRingSegment, facing the axis.</summary>
    public static void AddBoreSegment(SurfaceTool st, float x0, float r0, float x1, float r1, int angularSegments) =>
        AddRingSegment(st, x1, r1, x0, r0, angularSegments);

    /// <summary>A flat end cap at axial position x, radius r; a ring when
    /// there is a bore of radius innerR.</summary>
    public static void AddCap(SurfaceTool st, float x, float r, int angularSegments, bool outwardNormalIsNegativeX, float innerR = 0f)
    {
        for (int a = 0; a < angularSegments; a++)
        {
            float t0 = a * Mathf.Tau / angularSegments;
            float t1 = (a + 1) * Mathf.Tau / angularSegments;
            Vector3 p0 = new(x, r * Mathf.Cos(t0), r * Mathf.Sin(t0));
            Vector3 p1 = new(x, r * Mathf.Cos(t1), r * Mathf.Sin(t1));
            Vector3 q0 = new(x, innerR * Mathf.Cos(t0), innerR * Mathf.Sin(t0));
            Vector3 q1 = new(x, innerR * Mathf.Cos(t1), innerR * Mathf.Sin(t1));
            if (outwardNormalIsNegativeX)
            {
                AddTriangle(st, q0, p0, p1);
                if (innerR > 0f)
                    AddTriangle(st, q0, p1, q1);
            }
            else
            {
                AddTriangle(st, q0, p1, p0);
                if (innerR > 0f)
                    AddTriangle(st, q0, q1, p1);
            }
        }
    }

    /// <summary>The surfaces between two neighbouring profile samples: the
    /// outside, the bore, and where the bar ends after a bore, the end ring.</summary>
    public static void AddSection(SurfaceTool st, float x0, float r0, float in0, float x1, float r1, float in1, int angularSegments)
    {
        const float gone = 0.05f;
        if (r1 <= gone && in0 > 0f)
        {
            AddRingSegment(st, x0, r0, x1, in0, angularSegments);
            return;
        }
        if (r0 <= gone && in1 > 0f)
        {
            AddRingSegment(st, x0, in1, x1, r1, angularSegments);
            return;
        }
        AddRingSegment(st, x0, r0, x1, r1, angularSegments);
        if (in0 > 0f || in1 > 0f)
            AddBoreSegment(st, x0, in0, x1, in1, angularSegments);
    }
}
