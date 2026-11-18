using Godot;
using CncTrainer.Material;

namespace CncTrainer;

/// <summary>
/// Builds/updates the stock mesh from a LatheRadiusProfile as a surface of
/// revolution around the spindle axis, in fixed Z-chunks so a cut only
/// regenerates the chunk(s) it touched (brief section 5: "Regenerate only the
/// mesh for the changed range (chunked structure)"). World-axis convention
/// matches Main.cs: world X = machine Z (spindle axis), the cross-section
/// circle (radius = stock radius) lives in the world Y/Z plane. World units
/// are meters (see WorldScale.cs); Profile itself stays in mm (its own native
/// unit, and what the material-removal tests are written against).
///
/// [Tool]: builds the default (uncut) stock shape in the editor too, not just
/// at play time -- same reasoning as MachineBody.cs. The stock-size fields
/// live here (not on Main) specifically so the editor can show a correctly-
/// sized default stock without needing the running game.
///
/// No collision: unlike MachineBody/ConsoleStand, this one deliberately has
/// none. Its shape changes as material is cut, which would mean regenerating
/// a collision shape on every cut -- real cost for a part of the scene a
/// walking character isn't expected to reach anyway (it's inside the
/// headstock/chuck envelope). Revisit if that assumption changes.
/// </summary>
[Tool]
public partial class WorkpieceVisual : Node3D
{
    // Demo stock: 50mm diameter (25mm radius) bar, matches linuxcnc-config's
    // demo_facing_turning.ngc comment ("Stock: 50mm diameter round bar").
    /// <summary>Radius of the default bar (without hands-on setup).</summary>
    [Export] public float StockRadiusMm = 25.0f;
    /// <summary>Machine Z of the default bar's chuck end (mm).</summary>
    [Export] public float StockZMin = -85.0f;
    /// <summary>Machine Z of the default bar's free end (mm).</summary>
    [Export] public float StockZMax = 5.0f;
    /// <summary>No stock until SetProfile is called (hands-on setup).</summary>
    [Export] public bool StartEmpty;
    /// <summary>Segments around the bar's circumference in its mesh.</summary>
    [Export] public int AngularSegments = 24;
    /// <summary>Length of bar per mesh chunk (mm); a cut rebuilds only the chunks it touched.</summary>
    [Export] public float ChunkLengthMm = 4f;

    private int BucketsPerChunk => Mathf.Max(1, Mathf.RoundToInt(ChunkLengthMm / Profile.StepMm));
    /// <summary>Most chunks rebuilt in one frame, so a big cut does not stall the game.</summary>
    [Export] public int MaxChunkRebuildsPerCall = 4;

    /// <summary>The live radius profile -- Main.cs sweeps cuts into this same
    /// instance; this class only ever reads it to build the mesh.</summary>
    public LatheRadiusProfile Profile { get; private set; }

    private MeshInstance3D[] _chunkMeshes;
    private bool[] _chunkDirty;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        SetProfile(StartEmpty ? null : new LatheRadiusProfile(StockRadiusMm, StockZMin, StockZMax));
    }

    /// <summary>Shows this profile from now on; null = no stock.</summary>
    public void SetProfile(LatheRadiusProfile profile)
    {
        Profile = profile;
        _material ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.73f, 0.76f),
            Metallic = 0.7f,
            Roughness = 0.35f,
        };

        int chunkCount = profile == null ? 0 : Mathf.Max(1, Mathf.CeilToInt((float)(profile.BucketCount - 1) / BucketsPerChunk));
        _chunkMeshes = new MeshInstance3D[chunkCount];
        _chunkDirty = new bool[chunkCount];
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        for (int c = 0; c < chunkCount; c++)
        {
            var mi = new MeshInstance3D { MaterialOverride = _material };
            AddChild(mi);
            _chunkMeshes[c] = mi;
            _chunkDirty[c] = true;
        }
        RebuildDirtyChunks(chunkCount); // first build: do all of them regardless of the per-call cap
    }

    /// <summary>Call with the dirty bucket range reported by LatheRadiusProfile.Sweep.</summary>
    public void MarkDirty(int minBucketIndex, int maxBucketIndex)
    {
        // A chunk's mesh reaches one bucket into each neighbour (its end rings
        // and their normals), so a change within a bucket of an edge redraws both;
        // a thread's ring shows the radii up to a pitch further on.
        int c0 = Mathf.Max(0, minBucketIndex - 2 - Mathf.CeilToInt(Profile.MaxPitchMm / Profile.StepMm)) / BucketsPerChunk;
        int c1 = (maxBucketIndex + 1) / BucketsPerChunk;
        for (int c = c0; c <= Mathf.Min(_chunkDirty.Length - 1, c1); c++)
            _chunkDirty[c] = true;
    }

    /// <summary>Call once per frame (or on the scheduler's "high priority" slot,
    /// Phase 5): rebuilds up to MaxChunkRebuildsPerCall dirty chunks so a big cut
    /// doesn't spike frame time rebuilding everything at once.</summary>
    public void RebuildDirtyChunks(int budgetOverride = -1)
    {
        int budget = budgetOverride >= 0 ? budgetOverride : MaxChunkRebuildsPerCall;
        int rebuilt = 0;
        for (int c = 0; c < _chunkDirty.Length && rebuilt < budget; c++)
        {
            if (!_chunkDirty[c])
                continue;
            RebuildChunk(c);
            _chunkDirty[c] = false;
            rebuilt++;
        }
    }

    private void RebuildChunk(int chunkIndex)
    {
        int startBucket = chunkIndex * BucketsPerChunk;
        int endBucket = Mathf.Min(Profile.BucketCount - 1, startBucket + BucketsPerChunk);
        if (endBucket <= startBucket)
        {
            _chunkMeshes[chunkIndex].Mesh = null;
            return;
        }

        // Profile data is in mm (LatheRadiusProfile's own unit); convert
        // to meters here at the mesh-vertex boundary (see WorldScale.cs).
        const float m = WorldScale.MmToM;
        var st = new MeshBuffer();
        var clip = Profile.SquareHalfMm * m;
        int segments = clip == Vector2.Zero ? AngularSegments : 64;

        // One section past each end too (only for the normals), so the
        // normals on the chunk's end rings match the next chunk's: no seam.
        for (int i = Mathf.Max(0, startBucket - 1); i < Mathf.Min(Profile.BucketCount - 1, endBucket + 1); i++)
        {
            st.NormalsOnly = i < startBucket || i >= endBucket;
            float[] twist0 = clip == Vector2.Zero ? Helix(i, segments) : null, twist1 = clip == Vector2.Zero ? Helix(i + 1, segments) : null;
            if (twist0 == null && twist1 == null)
            {
                RevolutionMesh.AddSection(st, Profile.ZAt(i) * m, Profile.RadiusAt(i) * m, Profile.InnerAt(i) * m,
                    Profile.ZAt(i + 1) * m, Profile.RadiusAt(i + 1) * m, Profile.InnerAt(i + 1) * m, segments, clip);
                continue;
            }
            // Where the bar ends after a thread, the band closes down to the bore (the end face).
            bool endsAfter = Profile.RadiusAt(i + 1) <= 0f, endsBefore = Profile.RadiusAt(i) <= 0f;
            RevolutionMesh.AddRingSegment(st,
                Profile.ZAt(i) * m, twist0 ?? Ring((endsBefore ? Profile.InnerAt(i + 1) : Profile.RadiusAt(i)) * m, segments), twist0 != null ? HelixSetBack(Profile.PitchAt(i), segments) : null,
                Profile.ZAt(i + 1) * m, twist1 ?? Ring((endsAfter ? Profile.InnerAt(i) : Profile.RadiusAt(i + 1)) * m, segments), twist1 != null ? HelixSetBack(Profile.PitchAt(i + 1), segments) : null,
                endFace: endsAfter || endsBefore);
            if (!endsAfter && !endsBefore && (Profile.InnerAt(i) > 0f || Profile.InnerAt(i + 1) > 0f))
                RevolutionMesh.AddBoreSegment(st, Profile.ZAt(i) * m, Profile.InnerAt(i) * m, Profile.ZAt(i + 1) * m, Profile.InnerAt(i + 1) * m, segments);
        }

        st.NormalsOnly = false;
        if (startBucket == 0)
            RevolutionMesh.AddCap(st, Profile.ZAt(startBucket) * m, Profile.RadiusAt(startBucket) * m, segments,
                outwardNormalIsNegativeX: true, Profile.InnerAt(startBucket) * m, clip);
        if (endBucket == Profile.BucketCount - 1 && clip == Vector2.Zero && Helix(endBucket, segments) is { } endThread)
            RevolutionMesh.AddRingSegment(st, Profile.ZAt(endBucket) * m, endThread, HelixSetBack(Profile.PitchAt(endBucket), segments),
                Profile.ZAt(endBucket) * m, Ring(Profile.InnerAt(endBucket) * m, segments), null, endFace: true);
        else if (endBucket == Profile.BucketCount - 1)
            RevolutionMesh.AddCap(st, Profile.ZAt(endBucket) * m, Profile.RadiusAt(endBucket) * m, segments,
                outwardNormalIsNegativeX: false, Profile.InnerAt(endBucket) * m, clip);

        _chunkMeshes[chunkIndex].Mesh = st.Commit();
    }

    // A thread's helix: going round the bar its axial section advances one
    // pitch per turn. Each ring vertex takes the radius of a whole bucket
    // (no resampling, so the crests stay straight lines) and is set back
    // along the axis by the rest of the advance, under one bucket.
    private float[] Helix(int i, int segments)
    {
        float pitch = Profile.PitchAt(i);
        if (pitch <= 0f || Profile.RadiusAt(i) <= 0f)
            return null;
        var ring = new float[segments];
        for (int a = 0; a < segments; a++)
        {
            float advance = pitch * a / segments;
            float r = Profile.RadiusAt(i + Mathf.FloorToInt(advance / Profile.StepMm + 1e-4f));
            ring[a] = (r > 0f ? r : Profile.RadiusAt(i)) * WorldScale.MmToM;
        }
        return ring;
    }

    private float[] HelixSetBack(float pitch, int segments)
    {
        var back = new float[segments];
        for (int a = 0; a < segments && pitch > 0f; a++)
        {
            float advance = pitch * a / segments;
            back[a] = (advance - Mathf.FloorToInt(advance / Profile.StepMm + 1e-4f) * Profile.StepMm) * WorldScale.MmToM;
        }
        return back;
    }

    private static float[] Ring(float radius, int segments)
    {
        var ring = new float[segments];
        System.Array.Fill(ring, radius);
        return ring;
    }

    /// <summary>So a detached finished part (see WorkpiecePartBuilder) can
    /// visually match the stock it was just cut from instead of needing a
    /// second, independent material resource.</summary>
    public StandardMaterial3D StockVisualMaterial => _material;
}
