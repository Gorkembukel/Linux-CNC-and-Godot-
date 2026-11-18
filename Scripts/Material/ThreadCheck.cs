using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CncTrainer.Material;

/// <summary>An outside thread as measured: pitch and the major, minor and
/// pitch diameters (mm).</summary>
public readonly record struct ThreadForm(float PitchMm, float MajorMm, float MinorMm, float PitchDiameterMm);

/// <summary>A metric thread gauge's limits (ISO 965, 6g): what a GO ring
/// lets on and what a NO-GO ring must not.</summary>
public readonly record struct ThreadSpec(string Name, float NominalMm, float PitchMm, float PitchDiameterMinMm, float PitchDiameterMaxMm, float MajorMinMm, float MajorMaxMm)
{
    public static readonly ThreadSpec M20x2 = new("M20x2", 20f, 2f, 18.493f, 18.663f, 19.682f, 19.962f);
}

/// <summary>Reading a thread off a turned radius profile (radii every
/// stepMm along the axis). Pure C#, unit-testable.</summary>
public static class ThreadCheck
{
    private const float MinDepthMm = 0.1f;

    /// <summary>The thread around index, from the roots within ±windowMm;
    /// null when there are fewer than three roots there.</summary>
    public static ThreadForm? Measure(float[] radii, float stepMm, int index, float windowMm = 7f)
    {
        int half = Mathf.RoundToInt(windowMm / stepMm);
        int from = Mathf.Max(1, index - half), to = Mathf.Min(radii.Length - 2, index + half);
        if (to - from < 4)
            return null;
        float max = float.MinValue, min = float.MaxValue;
        for (int i = from; i <= to; i++)
        {
            max = Mathf.Max(max, radii[i]);
            min = Mathf.Min(min, radii[i]);
        }
        if (max - min < MinDepthMm || min <= 0f)
            return null;

        // A root is the middle of a run of buckets at the bottom of a groove.
        float floor = min + (max - min) * 0.25f;
        var roots = new List<float>();
        int start = -1;
        for (int i = from; i <= to + 1; i++)
        {
            bool low = i <= to && radii[i] <= floor;
            if (low && start < 0)
                start = i;
            else if (!low && start >= 0)
            {
                if (start > from && i <= to)
                    roots.Add((start + i - 1) / 2f);
                start = -1;
            }
        }
        if (roots.Count < 3)
            return null;
        var gaps = roots.Zip(roots.Skip(1), (a, b) => (b - a) * stepMm).OrderBy(g => g).ToArray();
        float pitch = gaps[gaps.Length / 2];

        // Pitch diameter: where the thread is as wide as the groove, i.e. the
        // median radius over one pitch.
        int first = Mathf.RoundToInt(roots[0]), span = Mathf.Max(2, Mathf.RoundToInt(pitch / stepMm));
        var onePitch = radii.Skip(first).Take(span).OrderBy(r => r).ToArray();
        float median = onePitch[onePitch.Length / 2];
        return new ThreadForm(Mathf.Snapped(pitch, 0.05f), 2f * max, 2f * min, 2f * median);
    }

    /// <summary>How many buckets from `end` going `direction` (+1 / -1)
    /// the thread runs before the profile goes smooth.</summary>
    public static int Length(float[] radii, float stepMm, int end, int direction, float pitchMm)
    {
        int window = Mathf.Max(2, Mathf.RoundToInt(pitchMm * 1.2f / stepMm));
        int i = end;
        for (; i >= 0 && i < radii.Length; i += direction)
        {
            float max = float.MinValue, min = float.MaxValue;
            for (int k = 0; k < window; k++)
            {
                int j = i + k * direction;
                if (j < 0 || j >= radii.Length)
                    break;
                max = Mathf.Max(max, radii[j]);
                min = Mathf.Min(min, radii[j]);
            }
            if (max - min < MinDepthMm * 2f)
                break;
        }
        return Mathf.Abs(i - end);
    }

    /// <summary>GO ring: why it will not go on, or null when it does.</summary>
    public static string GoRefusal(ThreadForm form, ThreadSpec spec) =>
        Mathf.Abs(form.MajorMm - spec.NominalMm) > 1.5f ? $"Bu mastar {spec.Name} içindir: parça Ø{form.MajorMm:0.0}"
        : Mathf.Abs(form.PitchMm - spec.PitchMm) > 0.05f ? $"Adım tutmuyor: dişin adımı {form.PitchMm:0.00} mm, mastar {spec.PitchMm:0.00} mm"
        : form.MajorMm > spec.MajorMaxMm + 0.005f ? $"Dış çap büyük: Ø{form.MajorMm:0.000} (en fazla {spec.MajorMaxMm:0.000})"
        : form.PitchDiameterMm > spec.PitchDiameterMaxMm + 0.005f ? $"Diş kalın: bölüm çapı {form.PitchDiameterMm:0.000} (en fazla {spec.PitchDiameterMaxMm:0.000}), daha derin aç"
        : null;

    /// <summary>NO-GO ring: true when it goes on (the thread is cut too deep).</summary>
    public static bool NoGoEnters(ThreadForm form, ThreadSpec spec) =>
        GoRefusal(form, spec) == null && form.PitchDiameterMm < spec.PitchDiameterMinMm - 0.005f;
}
