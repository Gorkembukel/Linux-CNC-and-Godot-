using Godot;

namespace CncTrainer.Material;

/// <summary>Rotationally symmetric cutter: a cylinder of RadiusMm and
/// LengthMm with either a rounded bottom edge (CornerRadiusMm: 0 = flat end
/// mill, RadiusMm = ball nose) or, when TipAngleDeg > 0, a conical point of
/// that included angle (drill, chamfer mill).</summary>
public readonly record struct CutterShape(float RadiusMm, float CornerRadiusMm, float LengthMm, float TipAngleDeg)
{
    /// <summary>Signed distance to the cutter surface (negative inside) at
    /// radial distance r from the tool axis and height h above the tip.
    /// Exact on and inside the surface, a lower bound outside it.</summary>
    public float Distance(float r, float h)
    {
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

    /// <summary>The largest step that keeps the grid under maxNodes, never
    /// finer than the requested one.</summary>
    public static float StepFor(Vector3 sizeMm, float requestedStepMm, int maxNodes = 8_000_000)
    {
        float volume = sizeMm.X * sizeMm.Y * sizeMm.Z;
        return Mathf.Max(requestedStepMm, Mathf.Pow(volume / maxNodes, 1f / 3f));
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

            var lo = (new Vector3(Mathf.Min(tip.X, top.X), Mathf.Min(tip.Y, top.Y), Mathf.Min(tip.Z, top.Z))
                      - Vector3.One * cutter.RadiusMm - Origin) / StepMm;
            var hi = (new Vector3(Mathf.Max(tip.X, top.X), Mathf.Max(tip.Y, top.Y), Mathf.Max(tip.Z, top.Z))
                      + Vector3.One * cutter.RadiusMm - Origin) / StepMm;
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
