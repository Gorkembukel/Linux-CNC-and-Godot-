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
    /// <summary>Bore radius per bucket (drilled / bored), 0 = solid.</summary>
    private readonly float[] _inner;
    /// <summary>Thread pitch a threading pass left on each bucket, 0 = none (for drawing the helix).</summary>
    private readonly float[] _pitch;

    public int BucketCount => _radius.Length;

    /// <summary>Stock that started as a block: its half sizes across the
    /// axis (section Y, Z). (0, 0) = round bar. See LatheSection.</summary>
    public Vector2 SquareHalfMm { get; private set; }

    public LatheRadiusProfile(float stockRadiusMm, float zMin, float zMax, float stepMm = 0.5f)
    {
        ZMin = zMin;
        ZMax = zMax;
        StepMm = stepMm;
        int count = Mathf.Max(2, Mathf.CeilToInt((zMax - zMin) / stepMm) + 1);
        _radius = new float[count];
        _inner = new float[count];
        _pitch = new float[count];
        for (int i = 0; i < count; i++)
            _radius[i] = stockRadiusMm;
    }

    /// <summary>A bar that already has a shape: radiiMm[0] is at zMin.</summary>
    public LatheRadiusProfile(float[] radiiMm, float zMin, float stepMm = 0.5f, float[] innerMm = null, Vector2 squareHalfMm = default)
    {
        SquareHalfMm = squareHalfMm;
        ZMin = zMin;
        ZMax = zMin + (radiiMm.Length - 1) * stepMm;
        StepMm = stepMm;
        _radius = radiiMm;
        _inner = innerMm ?? new float[radiiMm.Length];
        _pitch = new float[radiiMm.Length];
    }

    /// <summary>The radii from the chuck end up to where the bar ends (the
    /// first bucket cut through).</summary>
    public float[] LiveRadii() => _radius[..LiveCount()];

    /// <summary>Bore radii over the same buckets as LiveRadii.</summary>
    public float[] LiveInner() => _inner[..LiveCount()];

    private int LiveCount()
    {
        int count = 0;
        while (count < _radius.Length && _radius[count] > SeverEpsilonMm)
            count++;
        return count;
    }

    public float RadiusAt(int index) => _radius[Mathf.Clamp(index, 0, _radius.Length - 1)];
    public float InnerAt(int index) => _inner[Mathf.Clamp(index, 0, _inner.Length - 1)];
    public float PitchAt(int index) => _pitch[Mathf.Clamp(index, 0, _pitch.Length - 1)];

    /// <summary>The coarsest thread cut on this bar (0 = none).</summary>
    public float MaxPitchMm { get; private set; }

    /// <summary>Outside radius at any Z, linear between buckets.</summary>
    public float RadiusAtZ(float z)
    {
        float f = Mathf.Clamp((z - ZMin) / StepMm, 0f, _radius.Length - 1);
        int i = Mathf.Min((int)f, _radius.Length - 2);
        return Mathf.Lerp(_radius[i], _radius[i + 1], f - i);
    }
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
        var low = new float[i1 - i0 + 1];
        var high = new float[i1 - i0 + 1];

        for (int i = i0; i <= i1; i++)
        {
            float z = ZAt(i);
            float from, to;
            if (Mathf.Abs(z1 - z0) < 1e-6f)
            {
                // Facing/parting: no Z travel, the tool covers the whole X stroke.
                from = Mathf.Min(x0, x1);
                to = Mathf.Max(x0, x1);
            }
            else
                from = to = Mathf.Lerp(x0, x1, Mathf.Clamp((z - z0) / (z1 - z0), 0f, 1f));

            low[i - i0] = Mathf.Max(0f, from + toolNoseRadius);
            high[i - i0] = to + toolNoseRadius + PlainInsertHeightMm;
        }

        return CutSpans(i0, low, high, Mathf.Abs(z1 - z0));
    }

    /// <summary>How far a tool without a drawn insert reaches above its tip:
    /// the insert it stands for, below the shank.</summary>
    public const float PlainInsertHeightMm = 9f;

    /// <summary>Brings bucket i's outside down to cutRadius if it is above
    /// it. True when material was removed.</summary>
    private bool CutOutside(int i, float cutRadius, ref float maxDepthRemoved, ref float volume, ref int? severedIndex)
    {
        if (cutRadius >= _radius[i])
            return false;
        // Down to the bore the wall is gone: the ring falls away.
        if (cutRadius <= _inner[i] + SeverEpsilonMm)
            cutRadius = 0f;
        maxDepthRemoved = Mathf.Max(maxDepthRemoved, LatheSection.Reach(_radius[i], SquareHalfMm) - Mathf.Max(cutRadius, _inner[i]));
        // _radius[i] can only cross from >epsilon to <=epsilon ONCE:
        // cutRadius is clamped >=0, so once _radius[i] is already at
        // or below the epsilon, "cutRadius < _radius[i]" can't make
        // it cross again -- no extra per-bucket state needed.
        if (_radius[i] > SeverEpsilonMm && cutRadius <= SeverEpsilonMm)
            severedIndex = severedIndex is int existing ? Mathf.Min(existing, i) : i;
        float floor = Mathf.Max(cutRadius, _inner[i]);
        volume += Mathf.Max(0f, LatheSection.Area(_radius[i], SquareHalfMm) - LatheSection.Area(floor, SquareHalfMm)) * StepMm;
        _radius[i] = cutRadius;
        if (cutRadius == 0f)
            _inner[i] = 0f;
        return true;
    }

    /// <summary>
    /// Sweeps a real tool outline (ToolProfile) from one controlled-point
    /// position to the next. Turning, the outline sweeps a ring over each
    /// bucket, from its lowest to its highest reach, and the part loses that
    /// ring (CutSpan): a tool plunged at the centre opens only what its
    /// insert covers, the rim beyond it stays.
    /// threadPitchMm > 0: a threading pass, synchronised to the spindle, so
    /// over the move the tool's form repeats every pitch from anchorZ instead
    /// of smearing into a groove (the section of a helix).
    /// </summary>
    public SweepResult SweepProfile(Vector2 fromPos, Vector2 toPos, ToolProfile tool, float threadPitchMm = 0f, float anchorZ = 0f)
    {
        float z0 = fromPos.Y, z1 = toPos.Y, x0 = fromPos.X, x1 = toPos.X;
        float lo = Mathf.Min(z0, z1) + tool.ZMin, hi = Mathf.Max(z0, z1) + tool.ZMax;
        if (hi < ZMin - StepMm / 2f || lo > ZMax + StepMm / 2f)
            return SweepResult.None;
        int i0 = IndexAt(lo), i1 = IndexAt(hi);
        var low = new float[i1 - i0 + 1];
        var high = new float[i1 - i0 + 1];
        System.Array.Fill(low, float.PositiveInfinity);
        System.Array.Fill(high, float.NegativeInfinity);

        void Cover(int i, float x, float dz)
        {
            float under = tool.MinX(dz);
            if (float.IsNaN(under))
                return;
            float a = x + under, b = x + tool.MaxX(dz);
            low[i - i0] = Mathf.Min(low[i - i0], a <= 0f && b >= 0f ? 0f : Mathf.Min(Mathf.Abs(a), Mathf.Abs(b)));
            high[i - i0] = Mathf.Max(high[i - i0], Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
        }

        if (threadPitchMm > 0f)
        {
            for (int i = i0; i <= i1; i++)
            {
                float z = ZAt(i);
                if (z < Mathf.Min(z0, z1) - tool.ZMax || z > Mathf.Max(z0, z1) + tool.ZMax)
                    continue;
                float x = Mathf.Abs(z1 - z0) < 1e-6f ? Mathf.Min(x0, x1) : Mathf.Lerp(x0, x1, Mathf.Clamp((z - z0) / (z1 - z0), 0f, 1f));
                _pitch[i] = threadPitchMm;
                MaxPitchMm = Mathf.Max(MaxPitchMm, threadPitchMm);
                float phase = Mathf.PosMod(z - anchorZ, threadPitchMm);
                for (float dz = phase; dz <= tool.ZMax; dz += threadPitchMm)
                    Cover(i, x, dz);
            }
        }
        else
        {
            float length = new Vector2(x1 - x0, z1 - z0).Length();
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / (StepMm / 2f)));
            for (int s = 0; s <= samples; s++)
            {
                float t = (float)s / samples;
                float x = Mathf.Lerp(x0, x1, t), zt = Mathf.Lerp(z0, z1, t);
                for (int i = IndexAt(zt + tool.ZMin); i <= IndexAt(zt + tool.ZMax); i++)
                    Cover(i, x, ZAt(i) - zt);
            }
        }
        return CutSpans(i0, low, high, Mathf.Abs(z1 - z0));
    }

    /// <summary>As SweepProfile for a tool cutting from the inside (a boring
    /// bar, its outline mirrored so the cutting side faces outward).</summary>
    public SweepResult SweepProfileBore(Vector2 fromPos, Vector2 toPos, ToolProfile tool) =>
        SweepProfile(new Vector2(Mathf.Abs(fromPos.X), fromPos.Y), new Vector2(Mathf.Abs(toPos.X), toPos.Y), tool);

    private SweepResult CutSpans(int i0, float[] low, float[] high, float contactLength)
    {
        bool removedAny = false;
        float maxDepth = 0f, volume = 0f;
        int? severedIndex = null;
        for (int k = 0; k < low.Length; k++)
            if (low[k] <= high[k])
                removedAny |= CutSpan(i0 + k, low[k], high[k], ref maxDepth, ref volume, ref severedIndex);
        return removedAny
            ? new SweepResult(true, i0, i0 + low.Length - 1, maxDepth, contactLength, severedIndex, volume)
            : SweepResult.None;
    }

    /// <summary>The tool swept the ring a..b (radii) over bucket i. Reaching
    /// the outside it lowers the outside; reaching the bore or the axis it
    /// opens the bore. A ring wholly inside the wall (the insert pushed into
    /// the face away from the centre) would be a face groove, which two radii
    /// cannot hold: it opens toward whichever surface loses less.</summary>
    private bool CutSpan(int i, float a, float b, ref float maxDepth, ref float volume, ref int? severedIndex)
    {
        float outer = _radius[i], inner = _inner[i];
        if (outer <= SeverEpsilonMm || b <= inner || a >= outer)
            return false;
        float boreLoss = Mathf.Pi * (b * b - inner * inner);
        if (b >= outer - SeverEpsilonMm
            || (a > inner + SeverEpsilonMm && LatheSection.Area(outer, SquareHalfMm) - LatheSection.Area(a, SquareHalfMm) < boreLoss))
            return CutOutside(i, a, ref maxDepth, ref volume, ref severedIndex);
        maxDepth = Mathf.Max(maxDepth, b - inner);
        volume += boreLoss * StepMm;
        _inner[i] = b;
        return true;
    }

    /// <summary>Cutting from the inside: a drill on the centreline opens a
    /// hole of |x| + drillRadius behind its conical point (pointAngleDeg
    /// included, so the hole's bottom is that cone), a boring bar's edge
    /// (facing outward) opens the bore to x - noseRadius. Only where there is
    /// material; opening the bore to the outside drops the ring like a
    /// parting cut.</summary>
    public SweepResult SweepBore(Vector2 fromPos, Vector2 toPos, float toolNoseRadius, float drillRadius, float pointAngleDeg = 118f)
    {
        float z0 = fromPos.Y, z1 = toPos.Y, x0 = fromPos.X, x1 = toPos.X;
        bool drill = drillRadius > 0f;
        float slope = Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(pointAngleDeg, 1f, 180f) / 2f));
        float pointLength = drill && slope < 1e3f ? drillRadius / slope : 0f;
        float tipZ = Mathf.Min(z0, z1);
        if (Mathf.Max(z0, z1) + pointLength < ZMin - StepMm / 2f || tipZ > ZMax + StepMm / 2f)
            return SweepResult.None;
        int i0 = IndexAt(tipZ), i1 = IndexAt(Mathf.Max(z0, z1) + pointLength);
        bool removedAny = false;
        float maxDepth = 0f, volume = 0f;
        int? severedIndex = null;

        for (int i = i0; i <= i1; i++)
        {
            if (_radius[i] <= SeverEpsilonMm)
                continue;
            float z = ZAt(i);
            float x = Mathf.Abs(z1 - z0) < 1e-6f
                ? Mathf.Max(Mathf.Abs(x0), Mathf.Abs(x1))
                : Mathf.Abs(Mathf.Lerp(x0, x1, Mathf.Clamp((z - z0) / (z1 - z0), 0f, 1f)));
            if (z < tipZ - 1e-4f)
                continue;
            float cut = drill ? x + Mathf.Min(drillRadius, (z - tipZ) * slope) : x - toolNoseRadius;
            if (cut <= _inner[i])
                continue;
            float before = _inner[i];
            maxDepth = Mathf.Max(maxDepth, cut - before);
            if (cut >= _radius[i] - SeverEpsilonMm)
            {
                volume += Mathf.Pi * (_radius[i] * _radius[i] - before * before) * StepMm;
                _radius[i] = 0f;
                _inner[i] = 0f;
                severedIndex = severedIndex is int existing ? Mathf.Min(existing, i) : i;
            }
            else
            {
                volume += Mathf.Pi * (cut * cut - before * before) * StepMm;
                _inner[i] = cut;
            }
            removedAny = true;
        }

        return removedAny
            ? new SweepResult(true, i0, i1, maxDepth, Mathf.Abs(z1 - z0), severedIndex, volume)
            : SweepResult.None;
    }

    /// <summary>Call once a parting cut is detected (SweepResult.SeveredIndex)
    /// to split the stock: everything beyond `index` (toward the free/tip
    /// end) is now a physically separate piece. Zeroes those buckets in this
    /// profile (the remaining, chuck-held stock no longer "has" them) and
    /// returns their pre-sever radii so the caller can build the detached
    /// piece's own geometry. Buckets at/before `index` are untouched, so a
    /// later part-off further down the remaining stock works unmodified.</summary>
    public float[] Sever(int index) => Sever(index, out _);

    /// <summary>As Sever, also handing back the piece's bore radii.</summary>
    public float[] Sever(int index, out float[] removedInner)
    {
        removedInner = System.Array.Empty<float>();
        // Stop at the array's end OR at a bucket a PRIOR Sever already
        // zeroed, whichever comes first -- found live: without this, a
        // second part-off on the same remaining stock kept re-including the
        // first extraction's already-empty tail buckets as if they were
        // still live material (a degenerate zero-radius ghost on the new
        // piece's far end, growing with every subsequent part-off).
        // A blade wider than one bucket empties several at once: the piece
        // starts past the groove it left.
        int start = index + 1;
        while (start < _radius.Length && _radius[start] <= 0f)
            start++;
        int end = start;
        while (end < _radius.Length && _radius[end] > 0f)
            end++;

        int count = end - start;
        if (count <= 0)
            return System.Array.Empty<float>();

        var removed = new float[count];
        removedInner = new float[count];
        for (int i = start; i < end; i++)
        {
            removed[i - start] = _radius[i];
            removedInner[i - start] = _inner[i];
            _radius[i] = 0f;
            _inner[i] = 0f;
        }
        return removed;
    }
}
