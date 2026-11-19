using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Material;

namespace CncTrainer.Inspection;

/// <summary>What a drawing dimension measures.</summary>
public enum FeatureKind
{
    /// <summary>Turned: outside diameter at Position.X mm from the datum end.</summary>
    OutsideDiameter,
    /// <summary>Turned: bore diameter at Position.X mm from the datum end.</summary>
    BoreDiameter,
    /// <summary>Turned: length end to end.</summary>
    OverallLength,
    /// <summary>Milled: how far the surface under Position (x, z) lies
    /// below the surface under Reference (x, z).</summary>
    Depth,
    /// <summary>Milled: size through Position along Axis: a thickness when
    /// Position is in material, the gap between walls when it is in the open.</summary>
    Span,
    /// <summary>Surface roughness Ra in micrometres, as the machining left
    /// it (nominal 0, the upper tolerance is the most allowed).</summary>
    Roughness,
}

/// <summary>One toleranced dimension, engine-free (see PartFeature).
/// Axis is a unit direction for Span.</summary>
public readonly record struct FeatureSpec(
    string Label, FeatureKind Kind, Vector3 PositionMm, Vector3 ReferenceMm, Vector3 Axis,
    float NominalMm, float LowerMm, float UpperMm)
{
    private const float Epsilon = 1e-4f;

    public bool Accepts(float value) => value >= NominalMm + LowerMm - Epsilon && value <= NominalMm + UpperMm + Epsilon;

    public string Tolerance => System.FormattableString.Invariant($"{LowerMm:+0.###;-0.###;0}/{UpperMm:+0.###;-0.###;0}");
}

public readonly record struct FeatureResult(FeatureSpec Spec, float? MeasuredMm)
{
    public bool Pass => MeasuredMm is float value && Spec.Accepts(value);
}

/// <summary>Measures a finished part's features; null when this shape
/// cannot show that kind of feature.</summary>
public interface IPartGeometry
{
    float? Measure(FeatureSpec feature);
}

/// <summary>A shape that can lie either way round: the same part read from
/// its other end.</summary>
public interface IReversible
{
    IPartGeometry Reversed();
}

/// <summary>A part's shape plus the finish it came off the machine with.</summary>
public sealed class WithSurface : IPartGeometry, IReversible
{
    private readonly IPartGeometry _shape;
    private readonly float _raUm;

    public WithSurface(IPartGeometry shape, float raUm)
    {
        _shape = shape;
        _raUm = raUm;
    }

    public float? Measure(FeatureSpec feature) => feature.Kind == FeatureKind.Roughness ? _raUm : _shape.Measure(feature);

    public IPartGeometry Reversed() => _shape is IReversible reversible ? new WithSurface(reversible.Reversed(), _raUm) : this;
}

/// <summary>A turned part's profile: outside and bore radii every StepMm.
/// Reversed: the datum end is the profile's last entry.</summary>
public sealed class TurnedGeometry : IPartGeometry, IReversible
{
    private readonly float[] _radii, _inner;
    private readonly float _step;
    private readonly bool _reversed;

    public TurnedGeometry(float[] radiiMm, float[] innerMm, float stepMm, bool reversed = false)
    {
        _radii = radiiMm;
        _inner = innerMm;
        _step = stepMm;
        _reversed = reversed;
    }

    public IPartGeometry Reversed() => new TurnedGeometry(_radii, _inner, _step, !_reversed);

    public float? Measure(FeatureSpec feature)
    {
        int index = Mathf.Clamp(Mathf.RoundToInt(feature.PositionMm.X / _step), 0, _radii.Length - 1);
        if (_reversed)
            index = _radii.Length - 1 - index;
        return feature.Kind switch
        {
            FeatureKind.OutsideDiameter => 2f * _radii[index],
            FeatureKind.BoreDiameter => _inner[index] > 0f ? 2f * _inner[index] : null,
            FeatureKind.OverallLength => (_radii.Length - 1) * _step,
            _ => null,
        };
    }
}

/// <summary>A milled part's voxel field, in the stock's own frame (see VoxelStock).</summary>
public sealed class VoxelGeometry : IPartGeometry
{
    private readonly VoxelStock _stock;

    public VoxelGeometry(VoxelStock stock) => _stock = stock;

    public float? Measure(FeatureSpec feature) => feature.Kind switch
    {
        FeatureKind.Depth => _stock.SurfaceY(feature.ReferenceMm.X, feature.ReferenceMm.Z) is float top
                             && _stock.SurfaceY(feature.PositionMm.X, feature.PositionMm.Z) is float bottom
            ? top - bottom : null,
        FeatureKind.Span => _stock.Span(feature.PositionMm, feature.Axis.Normalized()),
        _ => null,
    };
}

/// <summary>Measures every feature on a part. A turned part can lie either
/// way round on the inspection table: it is read from whichever end
/// matches the drawing better, as an inspector would turn it.</summary>
public static class PartInspector
{
    public static IReadOnlyList<FeatureResult> Inspect(IReadOnlyList<FeatureSpec> features, IPartGeometry part)
    {
        var results = Measure(features, part);
        if (part is IReversible reversible)
        {
            var other = Measure(features, reversible.Reversed());
            if (Better(other, results))
                results = other;
        }
        return results;
    }

    private static List<FeatureResult> Measure(IReadOnlyList<FeatureSpec> features, IPartGeometry part) =>
        features.Select(f => new FeatureResult(f, part.Measure(f))).ToList();

    private static bool Better(List<FeatureResult> a, List<FeatureResult> b)
    {
        int passA = a.Count(r => r.Pass), passB = b.Count(r => r.Pass);
        return passA != passB ? passA > passB : Error(a) < Error(b);
    }

    private static float Error(List<FeatureResult> results) =>
        results.Sum(r => r.MeasuredMm is float v ? Mathf.Abs(v - r.Spec.NominalMm) : 1000f);
}
