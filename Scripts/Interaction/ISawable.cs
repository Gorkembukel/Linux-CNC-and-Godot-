using Godot;
using CncTrainer.Chips;

namespace CncTrainer.Interaction;

/// <summary>A cut a hand saw is about to make (all global): the cut plane
/// through Center with this Normal; the saw goes in along Down, its blade
/// across the section (WidthM wide, DepthM deep). AreaMm2 is what the saw
/// has to get through. AtMm says where, in the target's own terms; AngleDeg
/// how far the cut is turned from square to the stock (about the vertical).</summary>
public readonly record struct SawCut(Vector3 Center, Vector3 Normal, Vector3 Down, float WidthM, float DepthM, float AreaMm2, float AtMm, float AngleDeg = 0f)
{
    /// <summary>Along the blade.</summary>
    public Vector3 Across => Down.Cross(Normal).Normalized();
}

/// <summary>Something held still that a hand saw can cut through: stock in
/// a bench vise, a bar in a lathe chuck.</summary>
public interface ISawable
{
    /// <summary>The cut through the point the saw is held at, turned
    /// angleDeg from square, or null with why it cannot be made there.</summary>
    SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why);

    MaterialKind SawMaterial { get; }

    /// <summary>The saw has gone through: split the stock.</summary>
    void FinishCut(SawCut cut);

    public static ISawable Find(Node node)
    {
        for (; node != null; node = node.GetParent())
            if (node is ISawable sawable)
                return sawable;
            else if (node is Carryable { SawnBy: { } by })
                return by;
        return null;
    }
}
