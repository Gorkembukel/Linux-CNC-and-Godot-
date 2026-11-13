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
    [Export] public float StockRadiusMm = 25.0f;
    [Export] public float StockZMin = -85.0f;
    [Export] public float StockZMax = 5.0f;
    [Export] public int AngularSegments = 24;
    [Export] public int BucketsPerChunk = 40;
    [Export] public int MaxChunkRebuildsPerCall = 4;

    /// <summary>The live radius profile -- Main.cs sweeps cuts into this same
    /// instance; this class only ever reads it to build the mesh.</summary>
    public LatheRadiusProfile Profile { get; private set; }

    private MeshInstance3D[] _chunkMeshes;
    private bool[] _chunkDirty;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        RebuildFromScratch();
    }

    private void RebuildFromScratch()
    {
        Profile = new LatheRadiusProfile(StockRadiusMm, StockZMin, StockZMax);
        _material ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.73f, 0.76f),
            Metallic = 0.7f,
            Roughness = 0.35f,
        };

        int chunkCount = Mathf.Max(1, Mathf.CeilToInt((float)(Profile.BucketCount - 1) / BucketsPerChunk));
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
        int c0 = minBucketIndex / BucketsPerChunk;
        int c1 = maxBucketIndex / BucketsPerChunk;
        for (int c = Mathf.Max(0, c0 - 1); c <= Mathf.Min(_chunkDirty.Length - 1, c1 + 1); c++)
            _chunkDirty[c] = true; // neighbor chunks too, so the shared seam ring stays continuous
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

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = startBucket; i < endBucket; i++)
        {
            // Profile data is in mm (LatheRadiusProfile's own unit); convert
            // to meters here at the mesh-vertex boundary (see WorldScale.cs).
            float x0 = Profile.ZAt(i) * WorldScale.MmToM, x1 = Profile.ZAt(i + 1) * WorldScale.MmToM;
            float r0 = Profile.RadiusAt(i) * WorldScale.MmToM, r1 = Profile.RadiusAt(i + 1) * WorldScale.MmToM;

            for (int a = 0; a < AngularSegments; a++)
            {
                float t0 = a * Mathf.Tau / AngularSegments;
                float t1 = (a + 1) * Mathf.Tau / AngularSegments;
                Vector3 p00 = new(x0, r0 * Mathf.Cos(t0), r0 * Mathf.Sin(t0));
                Vector3 p01 = new(x0, r0 * Mathf.Cos(t1), r0 * Mathf.Sin(t1));
                Vector3 p10 = new(x1, r1 * Mathf.Cos(t0), r1 * Mathf.Sin(t0));
                Vector3 p11 = new(x1, r1 * Mathf.Cos(t1), r1 * Mathf.Sin(t1));

                AddTriangle(st, p00, p10, p11);
                AddTriangle(st, p00, p11, p01);
            }
        }

        if (startBucket == 0)
            AddCap(st, startBucket, outwardNormalIsNegativeX: true);
        if (endBucket == Profile.BucketCount - 1)
            AddCap(st, endBucket, outwardNormalIsNegativeX: false);

        st.GenerateNormals();
        _chunkMeshes[chunkIndex].Mesh = st.Commit();
    }

    private void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private void AddCap(SurfaceTool st, int bucketIndex, bool outwardNormalIsNegativeX)
    {
        float x = Profile.ZAt(bucketIndex) * WorldScale.MmToM;
        float r = Profile.RadiusAt(bucketIndex) * WorldScale.MmToM;
        var center = new Vector3(x, 0, 0);
        for (int a = 0; a < AngularSegments; a++)
        {
            float t0 = a * Mathf.Tau / AngularSegments;
            float t1 = (a + 1) * Mathf.Tau / AngularSegments;
            Vector3 p0 = new(x, r * Mathf.Cos(t0), r * Mathf.Sin(t0));
            Vector3 p1 = new(x, r * Mathf.Cos(t1), r * Mathf.Sin(t1));
            if (outwardNormalIsNegativeX)
                AddTriangle(st, center, p1, p0);
            else
                AddTriangle(st, center, p0, p1);
        }
    }
}
