using Godot;

namespace CncTrainer.Interaction;

/// <summary>Something a measuring tool can be held against. The player's
/// crosshair ray finds the nearest ancestor of the hit collider that
/// implements this while a gauge is in hand.</summary>
public interface IMeasurable
{
    /// <summary>What this gauge reads at a point (global) on the object,
    /// or null when the gauge does not apply to it.</summary>
    string Reading(HandToolKind gauge, Vector3 globalPoint);

    public static IMeasurable Find(Node node)
    {
        for (; node != null; node = node.GetParent())
            if (node is IMeasurable measurable)
                return measurable;
        return null;
    }
}

public static class Gauges
{
    public static bool IsGauge(HandToolKind kind) =>
        kind is HandToolKind.Caliper or HandToolKind.Micrometer or HandToolKind.DepthGauge or HandToolKind.PitchGauge;

    /// <summary>The pitch gauge's leaves held against a turned outside.</summary>
    public static string Pitch(ThreadSite? site)
    {
        if (site is not { } s || Material.ThreadCheck.Measure(s.RadiiMm, s.StepMm, s.Index) is not { } form)
            return "Diş tarağı: burada diş yok";
        return System.FormattableString.Invariant($"Diş tarağı: adım {form.PitchMm:0.00} mm (dış Ø{form.MajorMm:0.0})");
    }

    /// <summary>A caliper reads to 0.02 mm, a micrometer to 0.001 mm; a
    /// depth gauge does not measure diameters.</summary>
    public static string Diameter(HandToolKind gauge, float diameterMm) => gauge switch
    {
        HandToolKind.Caliper => System.FormattableString.Invariant($"Kumpas: Ø{Mathf.Round(diameterMm / 0.02f) * 0.02f:0.00} mm"),
        HandToolKind.Micrometer => System.FormattableString.Invariant($"Mikrometre: Ø{diameterMm:0.000} mm"),
        _ => null,
    };
}
