using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CncTrainer.Material;

public enum InsertShape { C80, D55, V35, T60, S90, W80, Round, Groove, Thread60, Custom }
public enum ToolHand { Right, Left, Neutral }

/// <summary>Engine-free description of a lathe tool's cutting insert (see
/// ToolGeometry, the Resource the editor shows). Lengths in mm, angles in
/// degrees. Approach angle: between the main cutting edge and the feed
/// toward the chuck (95 = can turn a square shoulder with 5 deg to spare).</summary>
public readonly record struct InsertParams(
    InsertShape Shape, float EdgeLengthMm, float NoseRadiusMm, float ApproachDeg, ToolHand Hand,
    float WidthMm = 3f, float DepthMm = 20f, float ThreadAngleDeg = 60f, Vector2[] CustomMm = null)
{
    public float IncludedDeg => Shape switch
    {
        InsertShape.C80 => 80f,
        InsertShape.D55 => 55f,
        InsertShape.V35 => 35f,
        InsertShape.T60 => 60f,
        InsertShape.S90 => 90f,
        InsertShape.W80 => 80f,
        _ => 0f,
    };
}

/// <summary>
/// The cutting outline of a lathe tool in its own frame: X radial (away
/// from the part's axis), Z along the spindle (toward the tailstock),
/// origin at the controlled point, i.e. the corner of the outline's bounding
/// box nearest the axis and the chuck (an outside tool: min X, min Z). What
/// it removes, swept along a path, is what the part loses: MinX is the
/// outline's underside at an axial offset, MaxX its top (a boring tool cuts
/// with that side).
/// </summary>
public sealed class ToolProfile
{
    private const float SampleMm = 0.05f;

    public IReadOnlyList<Vector2> Outline { get; }
    public float ZMin { get; }
    public float ZMax { get; }
    public float Height { get; }
    private readonly float[] _min, _max;

    /// <summary>Outline points as (x, z); shifted so the controlled point
    /// is the origin. internalTool: mirrored in X and anchored at max X, so
    /// the cutting side faces outward (a boring tool).</summary>
    public ToolProfile(IEnumerable<Vector2> outlineXz, bool internalTool = false)
    {
        var points = outlineXz.Select(p => internalTool ? new Vector2(-p.X, p.Y) : p).ToList();
        float anchorX = internalTool ? points.Max(p => p.X) : points.Min(p => p.X);
        float anchorZ = points.Min(p => p.Y);
        Outline = points.Select(p => new Vector2(p.X - anchorX, p.Y - anchorZ)).ToList();
        ZMin = 0f;
        ZMax = Outline.Max(p => p.Y);
        Height = Outline.Max(p => p.X) - Outline.Min(p => p.X);
        int n = Mathf.Max(2, Mathf.CeilToInt((ZMax - ZMin) / SampleMm) + 1);
        _min = new float[n];
        _max = new float[n];
        for (int i = 0; i < n; i++)
        {
            float z = ZMin + i * (ZMax - ZMin) / (n - 1);
            (_min[i], _max[i]) = Span(z);
        }
    }

    // Where the vertical line at axial offset z crosses the outline.
    private (float Min, float Max) Span(float z)
    {
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        for (int i = 0; i < Outline.Count; i++)
        {
            var a = Outline[i];
            var b = Outline[(i + 1) % Outline.Count];
            if ((z < Mathf.Min(a.Y, b.Y) - 1e-5f) || (z > Mathf.Max(a.Y, b.Y) + 1e-5f))
                continue;
            float x = Mathf.Abs(b.Y - a.Y) < 1e-6f ? Mathf.Min(a.X, b.X) : Mathf.Lerp(a.X, b.X, (z - a.Y) / (b.Y - a.Y));
            float xHigh = Mathf.Abs(b.Y - a.Y) < 1e-6f ? Mathf.Max(a.X, b.X) : x;
            lo = Mathf.Min(lo, x);
            hi = Mathf.Max(hi, xHigh);
        }
        return (lo, hi);
    }

    private float Lookup(float[] values, float dz)
    {
        if (dz < ZMin - 1e-4f || dz > ZMax + 1e-4f)
            return float.NaN;
        float t = Mathf.Clamp((dz - ZMin) / (ZMax - ZMin) * (values.Length - 1), 0f, values.Length - 1);
        int i = Mathf.Min((int)t, values.Length - 2);
        return Mathf.Lerp(values[i], values[i + 1], t - i);
    }

    /// <summary>Underside of the outline at axial offset dz from the
    /// controlled point; NaN where the tool is not.</summary>
    public float MinX(float dz) => Lookup(_min, dz);

    /// <summary>Top of the outline at axial offset dz; NaN where the tool is not.</summary>
    public float MaxX(float dz) => Lookup(_max, dz);

    /// <summary>The tool tip's own size along X and Z, for sanity checks.</summary>
    public Vector2 Size => new(Height, ZMax - ZMin);
}

/// <summary>Builds the outline of a standard insert (or a custom one).</summary>
public static class InsertOutline
{
    private const int ArcSegments = 10;

    public static List<Vector2> Build(InsertParams p)
    {
        var outline = p.Shape switch
        {
            InsertShape.Round => Circle(p.EdgeLengthMm / 2f),
            InsertShape.Groove => Groove(p.WidthMm, p.DepthMm, p.NoseRadiusMm),
            InsertShape.Thread60 => Thread(p.ThreadAngleDeg, p.NoseRadiusMm, p.DepthMm),
            InsertShape.Custom when p.CustomMm is { Length: >= 3 } custom => custom.ToList(),
            InsertShape.Custom => Groove(3f, 10f, 0f),
            _ => Polygon(p),
        };
        if (p.Hand == ToolHand.Left)
            outline = outline.Select(v => new Vector2(v.X, -v.Y)).Reverse().ToList();
        return outline;
    }

    /// <summary>A rhombus / triangle / square insert with its nose at the
    /// tip, main edge leaning back by (approach - 90) deg toward the
    /// tailstock, the nose rounded to its radius.</summary>
    private static List<Vector2> Polygon(InsertParams p)
    {
        float included = p.IncludedDeg;
        float main = Mathf.DegToRad(180f - p.ApproachDeg);
        float trailing = main - Mathf.DegToRad(included);
        Vector2 Dir(float angle) => new(Mathf.Sin(angle), Mathf.Cos(angle));
        var d1 = Dir(main);
        var d2 = Dir(trailing);
        float l = p.EdgeLengthMm;
        var corners = p.Shape is InsertShape.T60 or InsertShape.W80
            ? new List<Vector2> { Vector2.Zero, d1 * l, d2 * l }
            : new List<Vector2> { Vector2.Zero, d1 * l, (d1 + d2) * l, d2 * l };
        return RoundCorner(corners, 0, p.NoseRadiusMm);
    }

    /// <summary>Replaces corner `index` with a tangent arc of this radius.</summary>
    public static List<Vector2> RoundCorner(List<Vector2> corners, int index, float radius)
    {
        if (radius <= 0f)
            return corners;
        int n = corners.Count;
        var c = corners[index];
        var a = (corners[(index + n - 1) % n] - c).Normalized();
        var b = (corners[(index + 1) % n] - c).Normalized();
        float half = a.AngleTo(b);
        half = Mathf.Abs(half) / 2f;
        float back = radius / Mathf.Tan(half);
        var start = c + a * back;
        var end = c + b * back;
        var centre = c + (a + b).Normalized() * (radius / Mathf.Sin(half));
        float from = Mathf.Atan2(start.Y - centre.Y, start.X - centre.X);
        float to = Mathf.Atan2(end.Y - centre.Y, end.X - centre.X);
        float sweep = Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);
        var arc = new List<Vector2>();
        for (int i = 0; i <= ArcSegments; i++)
        {
            float t = from + sweep * i / ArcSegments;
            arc.Add(centre + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
        }
        var result = new List<Vector2>();
        for (int i = 0; i < n; i++)
            if (i == index)
                result.AddRange(arc);
            else
                result.Add(corners[i]);
        return result;
    }

    private static List<Vector2> Circle(float radius)
    {
        var points = new List<Vector2>();
        for (int i = 0; i < 32; i++)
        {
            float t = Mathf.Tau * i / 32;
            points.Add(new Vector2(radius - radius * Mathf.Cos(t), radius * Mathf.Sin(t)));
        }
        return points;
    }

    /// <summary>A grooving / parting blade: width along Z, both bottom corners rounded.</summary>
    private static List<Vector2> Groove(float width, float depth, float cornerRadius)
    {
        var corners = new List<Vector2> { Vector2.Zero, new(depth, 0f), new(depth, width), new(0f, width) };
        float r = Mathf.Min(cornerRadius, width / 2f - 0.01f);
        corners = RoundCorner(corners, 3, r);
        return RoundCorner(corners, 0, r);
    }

    /// <summary>A threading insert: a symmetric V of this included angle, its point truncated to the root radius.</summary>
    private static List<Vector2> Thread(float includedDeg, float tipRadius, float depth)
    {
        float half = Mathf.DegToRad(includedDeg / 2f);
        float spread = depth * Mathf.Tan(half);
        var corners = new List<Vector2> { new(0f, spread), new(depth, 2f * spread), new(depth, 0f) };
        return RoundCorner(corners, 0, tipRadius);
    }
}
