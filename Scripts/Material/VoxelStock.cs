using Godot;

namespace CncTrainer.Material;

/// <summary>Rotationally symmetric cutter: a cylinder of RadiusMm and
/// LengthMm with either a rounded bottom edge (CornerRadiusMm: 0 = flat end
/// mill, RadiusMm = ball nose) or, when TipAngleDeg > 0, a conical point of
/// that included angle (drill, chamfer mill).</summary>
public readonly record struct CutterShape(float RadiusMm, float CornerRadiusMm, float LengthMm, float TipAngleDeg, Vector2[] ProfileMm = null)
{
    /// <summary>A form cutter: its side profile (radius, height above the
    /// tip), points from the tip upward.</summary>
    public static CutterShape Form(Vector2[] profileMm, float lengthMm)
    {
        float radius = 0f;
        foreach (var p in profileMm)
            radius = Mathf.Max(radius, p.X);
        return new CutterShape(radius, 0f, Mathf.Max(lengthMm, profileMm[^1].Y), 0f, profileMm);
    }

    private float ProfileRadius(float h)
    {
        var p = ProfileMm;
        if (h <= p[0].Y)
            return p[0].X;
        for (int i = 1; i < p.Length; i++)
            if (h <= p[i].Y)
                return Mathf.Lerp(p[i - 1].X, p[i].X, (h - p[i - 1].Y) / Mathf.Max(p[i].Y - p[i - 1].Y, 1e-6f));
        return p[^1].X;
    }

    /// <summary>Signed distance to the cutter surface (negative inside) at
    /// radial distance r from the tool axis and height h above the tip.
    /// Exact on and inside the surface, a lower bound outside it.</summary>
    public float Distance(float r, float h)
    {
        if (ProfileMm is { Length: >= 2 })
            return Mathf.Max(r - ProfileRadius(h), Mathf.Max(-h, h - LengthMm));
        float body = Mathf.Max(r - RadiusMm, Mathf.Max(-h, h - LengthMm));
        if (TipAngleDeg > 0f)
        {
            float half = Mathf.DegToRad(TipAngleDeg / 2f);
            return Mathf.Max(body, r * Mathf.Cos(half) - h * Mathf.Sin(half));
        }
        float inner = RadiusMm - CornerRadiusMm;
        if (h < CornerRadiusMm && r > inner)
            return Mathf.Sqrt((r - inner) * (r - inner) + (h - CornerRadiusMm) * (h - CornerRadiusMm)) - CornerRadiusMm;
        return body;
    }
}

public readonly record struct VoxelCutResult(
    bool MaterialRemoved, float VolumeMm3, int MinIx, int MaxIx, int MinIy, int MaxIy, int MinIz, int MaxIz);

/// <summary>
/// Mill stock as a grid of signed distances to the material surface (mm,
/// positive inside), so a cutter in any orientation can remove material --
/// what a heightmap cannot do once a rotary axis turns the part. Pure C#
/// (Godot math types only), unit-testable without the engine.
///
/// Coordinates are the stock's own local frame in mm, Y up: a block spans
/// x in [-sizeX/2, sizeX/2], y in [-height, 0], z in [-sizeZ/2, sizeZ/2]; a
/// cylinder lies along X with its top line at y = 0. One layer of nodes
/// outside the stock on every side keeps the surface closed.
/// </summary>
public class VoxelStock
{
    public readonly float StepMm;
    public readonly int CountX, CountY, CountZ;
    public readonly Vector3 Origin;
    private readonly float[] _distance;

    private VoxelStock(Vector3 min, Vector3 max, float stepMm)
    {
        StepMm = stepMm;
        Origin = min - Vector3.One * stepMm;
        CountX = Mathf.CeilToInt((max.X - min.X) / stepMm) + 3;
        CountY = Mathf.CeilToInt((max.Y - min.Y) / stepMm) + 3;
        CountZ = Mathf.CeilToInt((max.Z - min.Z) / stepMm) + 3;
        _distance = new float[CountX * CountY * CountZ];
    }

    public static VoxelStock Block(float sizeXMm, float heightMm, float sizeZMm, float stepMm)
    {
        var min = new Vector3(-sizeXMm / 2f, -heightMm, -sizeZMm / 2f);
        var max = new Vector3(sizeXMm / 2f, 0f, sizeZMm / 2f);
        var stock = new VoxelStock(min, max, stepMm);
        stock.Fill(p => Mathf.Min(Mathf.Min(Mathf.Min(p.X - min.X, max.X - p.X), Mathf.Min(p.Y - min.Y, max.Y - p.Y)),
            Mathf.Min(p.Z - min.Z, max.Z - p.Z)));
        return stock;
    }

    public static VoxelStock CylinderAlongX(float lengthMm, float diameterMm, float stepMm)
    {
        float radius = diameterMm / 2f;
        var stock = new VoxelStock(new Vector3(-lengthMm / 2f, -diameterMm, -radius), new Vector3(lengthMm / 2f, 0f, radius), stepMm);
        stock.Fill(p => Mathf.Min(lengthMm / 2f - Mathf.Abs(p.X),
            radius - Mathf.Sqrt((p.Y + radius) * (p.Y + radius) + p.Z * p.Z)));
        return stock;
    }

    /// <summary>A turned part lying along X like CylinderAlongX: outside
    /// radii (and bore radii, 0 = solid) every profileStepMm from -X to +X,
    /// its top line at y = 0. squareHalfMm: the flats of the block it was
    /// turned from (LatheSection), (0, 0) = round.</summary>
    public static VoxelStock Revolved(float[] radiiMm, float[] innerMm, float profileStepMm, float stepMm, Vector2 squareHalfMm = default)
    {
        float length = (radiiMm.Length - 1) * profileStepMm;
        float maxR = Mathf.Max(0.1f, System.Linq.Enumerable.Max(radiiMm));
        float radius = squareHalfMm == Vector2.Zero ? maxR : Mathf.Min(maxR, squareHalfMm.X);
        float halfZ = squareHalfMm == Vector2.Zero ? maxR : Mathf.Min(maxR, squareHalfMm.Y);
        var stock = new VoxelStock(new Vector3(-length / 2f, -2f * radius, -halfZ), new Vector3(length / 2f, 0f, halfZ), stepMm);
        float At(float[] values, float x)
        {
            float t = Mathf.Clamp((x + length / 2f) / profileStepMm, 0f, values.Length - 1);
            int i = Mathf.Min((int)t, values.Length - 2);
            return Mathf.Lerp(values[i], values[i + 1], t - i);
        }
        stock.Fill(p =>
        {
            float r = Mathf.Sqrt((p.Y + radius) * (p.Y + radius) + p.Z * p.Z);
            float bore = At(innerMm, p.X);
            float d = Mathf.Min(length / 2f - Mathf.Abs(p.X), At(radiiMm, p.X) - r);
            if (squareHalfMm != Vector2.Zero)
                d = Mathf.Min(d, Mathf.Min(squareHalfMm.X - Mathf.Abs(p.Y + radius), squareHalfMm.Y - Mathf.Abs(p.Z)));
            return bore > 0f ? Mathf.Min(d, r - bore) : d;
        });
        return stock;
    }

    /// <summary>The largest step that keeps the grid under maxNodes, never
    /// finer than the requested one.</summary>
    public static float StepFor(Vector3 sizeMm, float requestedStepMm, int maxNodes = 8_000_000)
    {
        float volume = sizeMm.X * sizeMm.Y * sizeMm.Z;
        return Mathf.Max(requestedStepMm, Mathf.Pow(volume / maxNodes, 1f / 3f));
    }

    private VoxelStock(VoxelStock other)
    {
        StepMm = other.StepMm;
        Origin = other.Origin;
        CountX = other.CountX;
        CountY = other.CountY;
        CountZ = other.CountZ;
        _distance = (float[])other._distance.Clone();
    }

    public VoxelStock Copy() => new(this);

    /// <summary>Material volume (mm³).</summary>
    public float VolumeMm3()
    {
        float sum = 0f;
        foreach (float d in _distance)
            sum += Occupancy(d);
        return sum * StepMm * StepMm * StepMm;
    }

    /// <summary>Removes everything beyond a plane through pointMm, on the
    /// side the normal points to, and marginMm short of it on this side (a
    /// saw kerf is two margins wide).</summary>
    public VoxelCutResult RemoveBeyond(Vector3 pointMm, Vector3 normal, float marginMm)
    {
        normal = normal.Normalized();
        bool removed = false;
        float volume = 0f, cellVolume = StepMm * StepMm * StepMm;
        int minIx = int.MaxValue, maxIx = int.MinValue, minIy = int.MaxValue, maxIy = int.MinValue, minIz = int.MaxValue, maxIz = int.MinValue;
        for (int iz = 0; iz < CountZ; iz++)
            for (int iy = 0; iy < CountY; iy++)
                for (int ix = 0; ix < CountX; ix++)
                {
                    int i = Index(ix, iy, iz);
                    float old = _distance[i];
                    float d = -normal.Dot(NodePosition(ix, iy, iz) - pointMm) - marginMm;
                    if (d >= old || old <= -StepMm)
                        continue;
                    _distance[i] = d;
                    volume += (Occupancy(old) - Occupancy(d)) * cellVolume;
                    removed = true;
                    minIx = Mathf.Min(minIx, ix); maxIx = Mathf.Max(maxIx, ix);
                    minIy = Mathf.Min(minIy, iy); maxIy = Mathf.Max(maxIy, iy);
                    minIz = Mathf.Min(minIz, iz); maxIz = Mathf.Max(maxIz, iz);
                }
        return removed ? new VoxelCutResult(true, volume, minIx, maxIx, minIy, maxIy, minIz, maxIz) : default;
    }

    /// <summary>The box around the material (mm), or null when none is left.</summary>
    public (Vector3 Min, Vector3 Max)? MaterialBounds()
    {
        var min = Vector3.Inf;
        var max = -Vector3.Inf;
        for (int iz = 0; iz < CountZ; iz++)
            for (int iy = 0; iy < CountY; iy++)
                for (int ix = 0; ix < CountX; ix++)
                    if (_distance[Index(ix, iy, iz)] > 0f)
                    {
                        var p = NodePosition(ix, iy, iz);
                        min = min.Min(p);
                        max = max.Max(p);
                    }
        // The surface lies between the last node inside and the first outside.
        return min.X > max.X ? null : (min - Vector3.One * StepMm, max + Vector3.One * StepMm);
    }

    /// <summary>The same material on a grid just big enough for it
    /// (min..max, mm, in this grid's frame); the nodes line up with these.</summary>
    public VoxelStock Cropped(Vector3 min, Vector3 max)
    {
        var aligned = Origin + ((min - Origin) / StepMm).Floor() * StepMm + Vector3.One * StepMm;
        var cropped = new VoxelStock(aligned, max, StepMm);
        cropped.Fill(SampleDistance);
        return cropped;
    }

    /// <summary>Where a plane through pointMm (normal) meets the material:
    /// its middle, its size across (along down x normal) and along down, and
    /// its area -- sampled no finer than 1 mm. Null when it misses.</summary>
    public (Vector3 CentreMm, float WidthMm, float DepthMm, float AreaMm2)? PlaneSection(Vector3 pointMm, Vector3 normal, Vector3 down)
    {
        normal = normal.Normalized();
        var across = down.Cross(normal).Normalized();
        down = normal.Cross(across);
        float ds = Mathf.Max(StepMm, 1f);
        var far = Origin + new Vector3(CountX - 1, CountY - 1, CountZ - 1) * StepMm;
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            var c = new Vector3((corner & 1) == 0 ? Origin.X : far.X, (corner & 2) == 0 ? Origin.Y : far.Y, (corner & 4) == 0 ? Origin.Z : far.Z) - pointMm;
            u0 = Mathf.Min(u0, c.Dot(across)); u1 = Mathf.Max(u1, c.Dot(across));
            v0 = Mathf.Min(v0, c.Dot(down)); v1 = Mathf.Max(v1, c.Dot(down));
        }
        int count = 0;
        float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
        for (float u = u0; u <= u1; u += ds)
            for (float v = v0; v <= v1; v += ds)
                if (SampleDistance(pointMm + across * u + down * v) > 0f)
                {
                    count++;
                    a0 = Mathf.Min(a0, u); a1 = Mathf.Max(a1, u);
                    b0 = Mathf.Min(b0, v); b1 = Mathf.Max(b1, v);
                }
        if (count == 0)
            return null;
        return (pointMm + across * ((a0 + a1) / 2f) + down * ((b0 + b1) / 2f), a1 - a0 + ds, b1 - b0 + ds, count * ds * ds);
    }

    private void Fill(System.Func<Vector3, float> distance)
    {
        for (int iz = 0; iz < CountZ; iz++)
            for (int iy = 0; iy < CountY; iy++)
                for (int ix = 0; ix < CountX; ix++)
                    _distance[Index(ix, iy, iz)] = distance(NodePosition(ix, iy, iz));
    }

    private int Index(int ix, int iy, int iz) => (iz * CountY + iy) * CountX + ix;

    public Vector3 NodePosition(int ix, int iy, int iz) => Origin + new Vector3(ix, iy, iz) * StepMm;

    public float DistanceAt(int ix, int iy, int iz) => _distance[Index(ix, iy, iz)];

    /// <summary>Signed distance to the surface at any point (positive
    /// inside the material), interpolated between grid nodes.</summary>
    public float SampleDistance(Vector3 pointMm)
    {
        var g = (pointMm - Origin) / StepMm;
        int ix = Mathf.FloorToInt(g.X), iy = Mathf.FloorToInt(g.Y), iz = Mathf.FloorToInt(g.Z);
        if (ix < 0 || iy < 0 || iz < 0 || ix >= CountX - 1 || iy >= CountY - 1 || iz >= CountZ - 1)
            return -StepMm;
        float fx = g.X - ix, fy = g.Y - iy, fz = g.Z - iz;
        float Along(int y, int z) => Mathf.Lerp(DistanceAt(ix, y, z), DistanceAt(ix + 1, y, z), fx);
        return Mathf.Lerp(Mathf.Lerp(Along(iy, iz), Along(iy + 1, iz), fy), Mathf.Lerp(Along(iy, iz + 1), Along(iy + 1, iz + 1), fy), fz);
    }

    /// <summary>Height of the stock's surface under (x, z), seen from
    /// above; null where there is no material at all.</summary>
    public float? SurfaceY(float x, float z)
    {
        float step = StepMm / 4f, top = Origin.Y + (CountY - 1) * StepMm;
        float previous = SampleDistance(new Vector3(x, top, z));
        for (float y = top - step; y >= Origin.Y; y -= step)
        {
            float d = SampleDistance(new Vector3(x, y, z));
            if (d >= 0f)
                return previous < 0f ? y + step * d / (d - previous) : y;
            previous = d;
        }
        return null;
    }

    /// <summary>The size measured through a point along a direction: from a
    /// point in the open, the gap between the walls on either side (an
    /// inside size); from a point in the material, its thickness (an outside
    /// size). Null when one side never ends, as in a slot open at one end.</summary>
    public float? Span(Vector3 pointMm, Vector3 direction)
    {
        bool inMaterial = SampleDistance(pointMm) >= 0f;
        float forward = Reach(pointMm, direction, inMaterial), back = Reach(pointMm, -direction, inMaterial);
        return forward < 0f || back < 0f ? null : forward + back;
    }

    private float Reach(Vector3 from, Vector3 direction, bool inMaterial)
    {
        float step = StepMm / 4f, limit = (CountX + CountY + CountZ) * StepMm;
        float previous = SampleDistance(from);
        for (float t = step; t <= limit; t += step)
        {
            float d = SampleDistance(from + direction * t);
            if ((d >= 0f) != inMaterial)
                return t - step + step * previous / (previous - d);
            previous = d;
        }
        return -1f;
    }

    public bool IsMaterialAt(Vector3 pointMm)
    {
        var g = (pointMm - Origin) / StepMm;
        int ix = Mathf.RoundToInt(g.X), iy = Mathf.RoundToInt(g.Y), iz = Mathf.RoundToInt(g.Z);
        return ix >= 0 && iy >= 0 && iz >= 0 && ix < CountX && iy < CountY && iz < CountZ && DistanceAt(ix, iy, iz) > 0f;
    }

    /// <summary>Removes everything the cutter passes through while moving
    /// between two CONFIRMED poses (origin = tool tip, basis Y = tool axis
    /// from the tip toward the spindle, mm).</summary>
    public VoxelCutResult Cut(Transform3D from, Transform3D to, CutterShape cutter)
    {
        float tipMove = from.Origin.DistanceTo(to.Origin);
        float topMove = (from * new Vector3(0f, cutter.LengthMm, 0f)).DistanceTo(to * new Vector3(0f, cutter.LengthMm, 0f));
        float move = Mathf.Max(tipMove, topMove);
        if (move < 1e-5f)
            return default;
        int samples = Mathf.Clamp(Mathf.CeilToInt(move / (StepMm * 0.5f)), 1, 64);

        bool removed = false;
        float volume = 0f;
        int minIx = int.MaxValue, maxIx = int.MinValue, minIy = int.MaxValue, maxIy = int.MinValue, minIz = int.MaxValue, maxIz = int.MinValue;
        float cellVolume = StepMm * StepMm * StepMm;

        for (int s = 1; s <= samples; s++)
        {
            var pose = samples == 1 ? to : from.InterpolateWith(to, s / (float)samples);
            var tip = pose.Origin;
            var axis = pose.Basis.Y.Normalized();
            var top = tip + axis * cutter.LengthMm;

            // One step beyond the cutter: the nodes just outside it carry the
            // distance that places the cut surface between the grid points.
            var reach = Vector3.One * (cutter.RadiusMm + StepMm);
            var lo = (new Vector3(Mathf.Min(tip.X, top.X), Mathf.Min(tip.Y, top.Y), Mathf.Min(tip.Z, top.Z)) - reach - Origin) / StepMm;
            var hi = (new Vector3(Mathf.Max(tip.X, top.X), Mathf.Max(tip.Y, top.Y), Mathf.Max(tip.Z, top.Z)) + reach - Origin) / StepMm;
            int ix0 = Mathf.Max(0, Mathf.CeilToInt(lo.X)), ix1 = Mathf.Min(CountX - 1, Mathf.FloorToInt(hi.X));
            int iy0 = Mathf.Max(0, Mathf.CeilToInt(lo.Y)), iy1 = Mathf.Min(CountY - 1, Mathf.FloorToInt(hi.Y));
            int iz0 = Mathf.Max(0, Mathf.CeilToInt(lo.Z)), iz1 = Mathf.Min(CountZ - 1, Mathf.FloorToInt(hi.Z));

            for (int iz = iz0; iz <= iz1; iz++)
                for (int iy = iy0; iy <= iy1; iy++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        int i = Index(ix, iy, iz);
                        float old = _distance[i];
                        if (old <= -StepMm)
                            continue;
                        var rel = NodePosition(ix, iy, iz) - tip;
                        float h = rel.Dot(axis);
                        float r = (rel - axis * h).Length();
                        float d = cutter.Distance(r, h);
                        if (d >= old)
                            continue;

                        _distance[i] = d;
                        volume += (Occupancy(old) - Occupancy(d)) * cellVolume;
                        // Nodes further than one step inside do not shape the surface.
                        if (d >= StepMm)
                            continue;
                        removed = true;
                        minIx = Mathf.Min(minIx, ix); maxIx = Mathf.Max(maxIx, ix);
                        minIy = Mathf.Min(minIy, iy); maxIy = Mathf.Max(maxIy, iy);
                        minIz = Mathf.Min(minIz, iz); maxIz = Mathf.Max(maxIz, iz);
                    }
        }
        return removed ? new VoxelCutResult(true, volume, minIx, maxIx, minIy, maxIy, minIz, maxIz) : default;
    }

    private float Occupancy(float distance) => Mathf.Clamp(distance / StepMm + 0.5f, 0f, 1f);
}
