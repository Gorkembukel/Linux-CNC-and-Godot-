using Godot;

namespace CncTrainer.Inspection;

/// <summary>Direction a Span feature is measured along, in the stock's own frame.</summary>
public enum SpanAxis
{
    /// <summary>Along the stock's length.</summary>
    X,
    /// <summary>Vertical: a thickness or a wall height.</summary>
    Y,
    /// <summary>Across the stock (machine Y when the block sits as delivered).</summary>
    Z,
}

/// <summary>
/// One toleranced dimension on a work order's drawing. Coordinates:
/// turned parts measure along the axis from the datum end (Position.X);
/// milled parts use the stock's own frame in mm: X along the length from its
/// centre, Y up with 0 at the raw top face (so material is at negative Y), Z
/// across from its centre.
/// </summary>
[Tool, GlobalClass]
public partial class PartFeature : Resource
{
    /// <summary>How the dimension is called out on the drawing, e.g. "Ø20" or "Cep derinliği".</summary>
    [Export] public string Label = "";
    /// <summary>What is measured. OutsideDiameter / BoreDiameter / OverallLength
    /// are for turned parts; Depth and Span for milled ones.</summary>
    [Export] public FeatureKind Kind;
    /// <summary>Turned: distance from the datum end along the axis (only X is used).
    /// Milled: the point measured (Depth uses X and Z, Span the whole point).</summary>
    [Export] public Vector3 PositionMm;
    /// <summary>Depth only: the point whose surface the depth is measured
    /// from (usually a spot on the finished top face).</summary>
    [Export] public Vector3 ReferenceMm;
    /// <summary>Span only: the direction measured along.</summary>
    [Export] public SpanAxis Axis;
    /// <summary>The size on the drawing.</summary>
    [Export] public float NominalMm;
    /// <summary>Lower deviation, usually zero or negative (e.g. -0.05).</summary>
    [Export] public float LowerToleranceMm = -0.05f;
    /// <summary>Upper deviation, usually zero or positive (e.g. +0.05).</summary>
    [Export] public float UpperToleranceMm = 0.05f;

    public PartFeature() { }

    public PartFeature(string label, FeatureKind kind, float nominalMm, float lowerMm, float upperMm, Vector3 positionMm = default,
        Vector3 referenceMm = default, SpanAxis axis = SpanAxis.X)
    {
        Label = label; Kind = kind; NominalMm = nominalMm; LowerToleranceMm = lowerMm; UpperToleranceMm = upperMm;
        PositionMm = positionMm; ReferenceMm = referenceMm; Axis = axis;
    }

    public FeatureSpec ToSpec() => new(Label, Kind, PositionMm, ReferenceMm,
        Axis switch { SpanAxis.X => Vector3.Right, SpanAxis.Y => Vector3.Up, _ => Vector3.Back },
        NominalMm, LowerToleranceMm, UpperToleranceMm);
}
