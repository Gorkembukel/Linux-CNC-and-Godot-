using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Cross-section of turned stock that started as a rectangular block: the
/// circle the tool has turned it down to, cut off by the block's flat faces.
/// Angle t is measured in the section's (Y, Z) plane; half sizes (0, 0) = a
/// round bar, where every function is the plain circle.
/// </summary>
public static class LatheSection
{
    private const int Samples = 256;

    /// <summary>Distance from the axis to the edge at angle t.</summary>
    public static float Rho(float r, float t, Vector2 half)
    {
        if (half == Vector2.Zero)
            return r;
        float c = Mathf.Abs(Mathf.Cos(t)), s = Mathf.Abs(Mathf.Sin(t));
        float rho = r;
        if (c > 1e-6f)
            rho = Mathf.Min(rho, half.X / c);
        if (s > 1e-6f)
            rho = Mathf.Min(rho, half.Y / s);
        return rho;
    }

    public static float Area(float r, Vector2 half)
    {
        if (half == Vector2.Zero || r <= Mathf.Min(half.X, half.Y))
            return Mathf.Pi * r * r;
        float sum = 0f;
        for (int i = 0; i < Samples; i++)
        {
            float rho = Rho(r, (i + 0.5f) * Mathf.Tau / Samples, half);
            sum += rho * rho;
        }
        return 0.5f * sum * Mathf.Tau / Samples;
    }

    /// <summary>How far out the section reaches at its widest flat or arc:
    /// what a tool coming in meets first.</summary>
    public static float Reach(float r, Vector2 half) =>
        half == Vector2.Zero ? r : Mathf.Min(r, Mathf.Max(half.X, half.Y));

    /// <summary>Radius that just clears the block's corners.</summary>
    public static float Corner(Vector2 half) => half.Length();
}
