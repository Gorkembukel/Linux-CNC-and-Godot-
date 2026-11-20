using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Material;

/// <summary>What a caliper or a depth gauge reads on a milled part (voxel
/// stock), at a point in the stock's frame (mm). The gauge is put in where
/// the player aims: on the top face it reads the outside; in a pocket or a
/// blind hole, the inside size above its floor; in a hole drilled right
/// through (no floor under the point), the inside size just under the top
/// of the material around it. Engine-free.</summary>
public static class VoxelGauge
{
    /// <summary>How far round the aimed point to look for the top of the
    /// material around a through hole (mm).</summary>
    public const float SearchMm = 60f;

    public static string Read(VoxelStock stock, HandToolKind gauge, Vector3 point)
    {
        if (stock == null || gauge is not (HandToolKind.Caliper or HandToolKind.DepthGauge))
            return null;
        if (stock.SurfaceY(point.X, point.Z) is float surface)
        {
            float depth = Mathf.Max(0f, -surface);
            if (gauge == HandToolKind.DepthGauge)
                return System.FormattableString.Invariant($"Derinlik mastarı: {depth:0.00} mm  (parçanın üst yüzünden)");
            bool inPocket = depth > 0.1f;
            var at = new Vector3(point.X, inPocket ? surface + 0.3f : surface - 0.5f, point.Z);
            return $"Kumpas ({(inPocket ? "iç ölçü" : "dış ölçü")}): X {Size(stock.Span(at, Vector3.Right))}, Y {Size(stock.Span(at, Vector3.Back))}";
        }

        // Nothing under the point: a hole right through, or off the part.
        if (TopAround(stock, point) is not float top)
            return "Burada ölçülecek malzeme yok";
        var inHole = new Vector3(point.X, top - 1f, point.Z);
        float? x = stock.Span(inHole, Vector3.Right), y = stock.Span(inHole, Vector3.Back);
        if (x == null && y == null)
            return "Burada ölçülecek malzeme yok";
        return gauge == HandToolKind.DepthGauge
            ? "Derinlik mastarı: dip yok, delik boydan boya"
            : $"Kumpas (iç ölçü, boydan boya delik): X {Size(x)}, Y {Size(y)}";
    }

    /// <summary>The top of the material nearest the point, looking out round
    /// it in rings; null when there is none within reach.</summary>
    private static float? TopAround(VoxelStock stock, Vector3 point)
    {
        for (float r = stock.StepMm; r <= SearchMm; r += stock.StepMm)
            for (int k = 0; k < 16; k++)
            {
                float a = Mathf.Tau * k / 16f;
                if (stock.SurfaceY(point.X + r * Mathf.Cos(a), point.Z + r * Mathf.Sin(a)) is float top)
                    return top;
            }
        return null;
    }

    private static string Size(float? mm) =>
        mm is float value ? System.FormattableString.Invariant($"{Mathf.Round(value / 0.02f) * 0.02f:0.00} mm") : "açık";
}
