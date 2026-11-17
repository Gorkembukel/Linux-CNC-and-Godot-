using Godot;

namespace CncTrainer.Interaction;

/// <summary>The 24 ways a part can sit square in a fixture: any rotation
/// snapped to quarter turns about the frame's axes.</summary>
public static class SquareTurn
{
    private static readonly Vector3[] Axes =
        { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };

    public static Basis Snap(Basis basis)
    {
        var b = basis.Orthonormalized();
        var x = Nearest(b.X, Vector3.Zero);
        var y = Nearest(b.Y, x);
        return new Basis(x, y, x.Cross(y));
    }

    // The axis closest to v, skipping the one already taken by `not` (and its opposite).
    private static Vector3 Nearest(Vector3 v, Vector3 not)
    {
        Vector3 best = Vector3.Zero;
        float bestDot = float.NegativeInfinity;
        foreach (var axis in Axes)
        {
            if (Mathf.Abs(axis.Dot(not)) > 0.5f)
                continue;
            float dot = v.Dot(axis);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = axis;
            }
        }
        return best;
    }

    /// <summary>Size along the frame's X, Y, Z of a box of this size turned this way.</summary>
    public static Vector3 Extents(Basis turn, Vector3 size) => (turn * size).Abs();

    /// <summary>A quarter turn about one of the frame's axes, applied after `turn`.</summary>
    public static Basis QuarterTurn(Basis turn, Vector3 about) => Snap(new Basis(about, Mathf.Pi / 2f) * turn);
}
