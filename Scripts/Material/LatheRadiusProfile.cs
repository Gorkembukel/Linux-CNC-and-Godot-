using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Brief section 5: "fixed-step (configurable) radius array along the Z axis.
/// Sweep the tool tip geometry (including nose radius) between every two
/// confirmed samples and update the radius values with min."
///
/// Pure C# (only Godot.Vector2/Mathf, no engine/node dependency) so it's
/// unit-testable without the engine -- see Tests/LatheRadiusProfileTests.cs.
///
/// Simplification, stated plainly rather than left for someone to discover: the
/// tool nose is modelled as a single point offset outward by ToolNoseRadius
/// along +X, not a true tangent circle against the swept path. A true
/// nose-radius sweep needs to know the compensation side and whether the move
/// is turning/facing/profiling a corner; the simple offset is correct for the
/// common case (straight OD turning and facing cuts, which is all the demo
/// program does) and only becomes visibly wrong right at a sharp corner, where
/// it under-cuts by up to ToolNoseRadius for one bucket. Documented here and in
/// PLAN.md rather than solved, given the training-sim (not CAM-grade) scope.
/// </summary>
public class LatheRadiusProfile : IMaterialModel
{
    public readonly float ZMin, ZMax, StepMm;
    private readonly float[] _radius;

    public int BucketCount => _radius.Length;

    public LatheRadiusProfile(float stockRadiusMm, float zMin, float zMax, float stepMm = 0.5f)
    {
        ZMin = zMin;
        ZMax = zMax;
        StepMm = stepMm;
        int count = Mathf.Max(2, Mathf.CeilToInt((zMax - zMin) / stepMm) + 1);
        _radius = new float[count];
        for (int i = 0; i < count; i++)
            _radius[i] = stockRadiusMm;
    }

    /// <summary>A bar that already has a shape: radiiMm[0] is at zMin.</summary>
    public LatheRadiusProfile(float[] radiiMm, float zMin, float stepMm = 0.5f)
    {
        ZMin = zMin;
        ZMax = zMin + (radiiMm.Length - 1) * stepMm;
        StepMm = stepMm;
        _radius = radiiMm;
    }

    /// <summary>The radii from the chuck end up to where the bar ends (the
    /// first bucket cut through).</summary>
    public float[] LiveRadii()
    {
        int count = 0;
        while (count < _radius.Length && _radius[count] > SeverEpsilonMm)
            count++;
        return _radius[..count];
    }

    public float RadiusAt(int index) => _radius[Mathf.Clamp(index, 0, _radius.Length - 1)];
    public float ZAt(int index) => ZMin + index * StepMm;

    public int IndexAt(float z) => Mathf.Clamp(Mathf.RoundToInt((z - ZMin) / StepMm), 0, _radius.Length - 1);

    // Below this, a bucket counts as "cut all the way through" for severance
    // detection (Sweep's SeveredIndex / Sever below) -- matches the clamp
    // floor parting/facing moves already drive radius to (see
    // Sweep_HandlesFacingMoves_WithNoZTravel).
    private const float SeverEpsilonMm = 0.05f;

    public SweepResult Sweep(Vector2 fromPos, Vector2 toPos, float toolNoseRadius, float feedPerRev, float spindleRpm)
    {
        float z0 = fromPos.Y, z1 = toPos.Y;
        float x0 = fromPos.X, x1 = toPos.X;

        // A tool moving beyond either end of the bar touches nothing.
        if (Mathf.Max(z0, z1) < ZMin - StepMm / 2f || Mathf.Min(z0, z1) > ZMax + StepMm / 2f)
            return SweepResult.None;

        int i0 = IndexAt(Mathf.Min(z0, z1));
        int i1 = IndexAt(Mathf.Max(z0, z1));

        bool removedAny = false;
        float maxDepthRemoved = 0f, volume = 0f;
        int? severedIndex = null;

        for (int i = i0; i <= i1; i++)
        {
            float z = ZAt(i);
            float xAtZ;
            if (Mathf.Abs(z1 - z0) < 1e-6f)
                xAtZ = Mathf.Min(x0, x1); // facing/parting: no Z travel, use the deeper (cutting) X
            else
                xAtZ = Mathf.Lerp(x0, x1, Mathf.Clamp((z - z0) / (z1 - z0), 0f, 1f));

            float cutRadius = Mathf.Max(0f, xAtZ + toolNoseRadius);
            if (cutRadius < _radius[i])
            {
                maxDepthRemoved = Mathf.Max(maxDepthRemoved, _radius[i] - cutRadius);
                // _radius[i] can only cross from >epsilon to <=epsilon ONCE:
                // cutRadius is clamped >=0, so once _radius[i] is already at
                // or below the epsilon, "cutRadius < _radius[i]" can't make
                // it cross again -- no extra per-bucket state needed.
                if (_radius[i] > SeverEpsilonMm && cutRadius <= SeverEpsilonMm)
                    severedIndex = severedIndex is int existing ? Mathf.Min(existing, i) : i;
                volume += Mathf.Pi * (_radius[i] * _radius[i] - cutRadius * cutRadius) * StepMm;
                _radius[i] = cutRadius;
                removedAny = true;
            }
        }

        if (!removedAny)
            return SweepResult.None;

        float contactLength = Mathf.Abs(z1 - z0);
        return new SweepResult(true, i0, i1, maxDepthRemoved, contactLength, severedIndex, volume);
    }

    /// <summary>Call once a parting cut is detected (SweepResult.SeveredIndex)
    /// to split the stock: everything beyond `index` (toward the free/tip
    /// end) is now a physically separate piece. Zeroes those buckets in this
    /// profile (the remaining, chuck-held stock no longer "has" them) and
    /// returns their pre-sever radii so the caller can build the detached
    /// piece's own geometry. Buckets at/before `index` are untouched, so a
    /// later part-off further down the remaining stock works unmodified.</summary>
    public float[] Sever(int index)
    {
        // Stop at the array's end OR at a bucket a PRIOR Sever already
        // zeroed, whichever comes first -- found live: without this, a
        // second part-off on the same remaining stock kept re-including the
        // first extraction's already-empty tail buckets as if they were
        // still live material (a degenerate zero-radius ghost on the new
        // piece's far end, growing with every subsequent part-off).
        int end = index + 1;
        while (end < _radius.Length && _radius[end] > 0f)
            end++;

        int count = end - index - 1;
        if (count <= 0)
            return System.Array.Empty<float>();

        var removed = new float[count];
        for (int i = index + 1; i < end; i++)
        {
            removed[i - index - 1] = _radius[i];
            _radius[i] = 0f;
        }
        return removed;
    }
}
