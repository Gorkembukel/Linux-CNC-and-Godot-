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

    public float RadiusAt(int index) => _radius[Mathf.Clamp(index, 0, _radius.Length - 1)];
    public float ZAt(int index) => ZMin + index * StepMm;

    public int IndexAt(float z) => Mathf.Clamp(Mathf.RoundToInt((z - ZMin) / StepMm), 0, _radius.Length - 1);

    public SweepResult Sweep(Vector2 fromPos, Vector2 toPos, float toolNoseRadius, float feedPerRev, float spindleRpm)
    {
        float z0 = fromPos.Y, z1 = toPos.Y;
        float x0 = fromPos.X, x1 = toPos.X;

        int i0 = IndexAt(Mathf.Min(z0, z1));
        int i1 = IndexAt(Mathf.Max(z0, z1));

        bool removedAny = false;
        float maxDepthRemoved = 0f;

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
                _radius[i] = cutRadius;
                removedAny = true;
            }
        }

        if (!removedAny)
            return SweepResult.None;

        float contactLength = Mathf.Abs(z1 - z0);
        return new SweepResult(true, i0, i1, maxDepthRemoved, contactLength);
    }
}
