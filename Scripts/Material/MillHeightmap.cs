using Godot;

namespace CncTrainer.Material;

public readonly struct MillSweepResult
{
    public readonly bool MaterialRemoved;
    public readonly int MinIx, MaxIx, MinIy, MaxIy;
    public readonly float MaxDepthMm;
    public readonly float VolumeMm3;

    public MillSweepResult(int minIx, int maxIx, int minIy, int maxIy, float maxDepthMm, float volumeMm3)
    {
        MaterialRemoved = true;
        MinIx = minIx; MaxIx = maxIx; MinIy = minIy; MaxIy = maxIy;
        MaxDepthMm = maxDepthMm;
        VolumeMm3 = volumeMm3;
    }
}

/// <summary>
/// Mill stock as a grid of surface heights (mm) over the block's XY
/// footprint, cut by a flat end mill. Machine coordinates: XY origin at the
/// block's centre, Z = 0 on its uncut top face. Pure C# (Godot math types
/// only) so it is unit-testable without the engine.
///
/// A heightmap cannot represent undercuts, which a 3-axis mill with a flat
/// cutter cannot produce anyway.
/// </summary>
public class MillHeightmap
{
    public readonly float XMin, YMin, StepMm, BottomZ;
    public readonly int CountX, CountY;
    private readonly float[] _height;

    public MillHeightmap(float sizeXMm, float sizeYMm, float thicknessMm, float stepMm = 0.5f)
    {
        StepMm = stepMm;
        XMin = -sizeXMm / 2f;
        YMin = -sizeYMm / 2f;
        BottomZ = -thicknessMm;
        CountX = Mathf.RoundToInt(sizeXMm / stepMm) + 1;
        CountY = Mathf.RoundToInt(sizeYMm / stepMm) + 1;
        _height = new float[CountX * CountY];
    }

    public float XAt(int ix) => XMin + ix * StepMm;
    public float YAt(int iy) => YMin + iy * StepMm;

    public float HeightAt(int ix, int iy) =>
        _height[Mathf.Clamp(iy, 0, CountY - 1) * CountX + Mathf.Clamp(ix, 0, CountX - 1)];

    /// <summary>Lowers every grid node the cutter's flat bottom passes over
    /// while the tool tip moves in a straight line between two CONFIRMED
    /// positions (machine mm).</summary>
    public MillSweepResult Sweep(Vector3 from, Vector3 to, float toolRadiusMm)
    {
        int ix0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(from.X, to.X) - toolRadiusMm - XMin) / StepMm));
        int ix1 = Mathf.Min(CountX - 1, Mathf.FloorToInt((Mathf.Max(from.X, to.X) + toolRadiusMm - XMin) / StepMm));
        int iy0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(from.Y, to.Y) - toolRadiusMm - YMin) / StepMm));
        int iy1 = Mathf.Min(CountY - 1, Mathf.FloorToInt((Mathf.Max(from.Y, to.Y) + toolRadiusMm - YMin) / StepMm));

        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float a = dx * dx + dy * dy;
        float r2 = toolRadiusMm * toolRadiusMm;

        bool removed = false;
        int minIx = int.MaxValue, maxIx = int.MinValue, minIy = int.MaxValue, maxIy = int.MinValue;
        float maxDepth = 0f, volume = 0f;

        for (int iy = iy0; iy <= iy1; iy++)
        {
            float py = from.Y - YAt(iy);
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float px = from.X - XAt(ix);
                float c = px * px + py * py - r2;

                // The node is under the cutter for the travel fraction
                // [t0, t1]; tool Z is linear in t, so its lowest point over
                // the node is at one end of that interval.
                float t0 = 0f, t1 = 1f;
                if (a < 1e-9f)
                {
                    if (c > 0f)
                        continue;
                }
                else
                {
                    float b = px * dx + py * dy;
                    float disc = b * b - a * c;
                    if (disc < 0f)
                        continue;
                    float root = Mathf.Sqrt(disc);
                    t0 = Mathf.Max(0f, (-b - root) / a);
                    t1 = Mathf.Min(1f, (-b + root) / a);
                    if (t0 > t1)
                        continue;
                }

                float cutZ = Mathf.Max(BottomZ, from.Z + Mathf.Min(t0 * dz, t1 * dz));
                int i = iy * CountX + ix;
                if (cutZ >= _height[i])
                    continue;

                float depth = _height[i] - cutZ;
                _height[i] = cutZ;
                removed = true;
                maxDepth = Mathf.Max(maxDepth, depth);
                volume += depth * StepMm * StepMm;
                minIx = Mathf.Min(minIx, ix); maxIx = Mathf.Max(maxIx, ix);
                minIy = Mathf.Min(minIy, iy); maxIy = Mathf.Max(maxIy, iy);
            }
        }

        return removed ? new MillSweepResult(minIx, maxIx, minIy, maxIy, maxDepth, volume) : default;
    }
}
