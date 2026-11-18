using Godot;
using CncTrainer.Material;

namespace CncTrainer;

/// <summary>
/// Draws a MillHeightmap as a chunked grid mesh so a cut only rebuilds the
/// chunks it touched. Local axes: X = machine X, Y (up) = machine Z,
/// Z = -machine Y; this node's origin is the centre of the stock's top face.
/// </summary>
[Tool]
public partial class MillStockVisual : Node3D
{
    /// <summary>Stock length along X.</summary>
    [Export] public float StockSizeXMm = 120f;
    /// <summary>Stock width along Y.</summary>
    [Export] public float StockSizeYMm = 80f;
    /// <summary>Stock height.</summary>
    [Export] public float StockThicknessMm = 30f;
    /// <summary>Grid spacing of the cut surface: smaller is finer and slower.</summary>
    [Export] public float StepMm = 0.5f;
    /// <summary>Grid cells per mesh chunk; a cut rebuilds only the chunks it touched.</summary>
    [Export] public int CellsPerChunk = 40;
    /// <summary>Most chunks rebuilt in one frame, so a big cut does not stall the game.</summary>
    [Export] public int MaxChunkRebuildsPerCall = 4;

    public MillHeightmap Heightmap { get; private set; }

    private int _chunksX, _chunksY;
    private MeshInstance3D[] _chunkMeshes;
    private bool[] _chunkDirty;

    public override void _Ready()
    {
        Heightmap = new MillHeightmap(StockSizeXMm, StockSizeYMm, StockThicknessMm, StepMm);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.79f, 0.82f),
            Metallic = 0.6f,
            Roughness = 0.4f,
        };

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _chunksX = Mathf.CeilToInt((Heightmap.CountX - 1) / (float)CellsPerChunk);
        _chunksY = Mathf.CeilToInt((Heightmap.CountY - 1) / (float)CellsPerChunk);
        _chunkMeshes = new MeshInstance3D[_chunksX * _chunksY];
        _chunkDirty = new bool[_chunkMeshes.Length];
        for (int c = 0; c < _chunkMeshes.Length; c++)
        {
            _chunkMeshes[c] = new MeshInstance3D { MaterialOverride = material };
            AddChild(_chunkMeshes[c]);
            _chunkDirty[c] = true;
        }
        RebuildDirtyChunks(_chunkMeshes.Length);
    }

    public void MarkDirty(MillSweepResult r)
    {
        // A changed node also changes the normals of its neighbours, which
        // can sit in the next chunk over.
        int cx0 = Mathf.Max(0, (r.MinIx - 2) / CellsPerChunk), cx1 = Mathf.Min(_chunksX - 1, (r.MaxIx + 1) / CellsPerChunk);
        int cy0 = Mathf.Max(0, (r.MinIy - 2) / CellsPerChunk), cy1 = Mathf.Min(_chunksY - 1, (r.MaxIy + 1) / CellsPerChunk);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                _chunkDirty[cy * _chunksX + cx] = true;
    }

    public void RebuildDirtyChunks(int budgetOverride = -1)
    {
        int budget = budgetOverride >= 0 ? budgetOverride : MaxChunkRebuildsPerCall;
        for (int c = 0; c < _chunkDirty.Length && budget > 0; c++)
        {
            if (!_chunkDirty[c])
                continue;
            RebuildChunk(c % _chunksX, c / _chunksX);
            _chunkDirty[c] = false;
            budget--;
        }
    }

    private Vector3 NodePoint(int ix, int iy) =>
        new Vector3(Heightmap.XAt(ix), Heightmap.HeightAt(ix, iy), -Heightmap.YAt(iy)) * WorldScale.MmToM;

    private void RebuildChunk(int cx, int cy)
    {
        var hm = Heightmap;
        int ix0 = cx * CellsPerChunk, ix1 = Mathf.Min(hm.CountX - 1, ix0 + CellsPerChunk);
        int iy0 = cy * CellsPerChunk, iy1 = Mathf.Min(hm.CountY - 1, iy0 + CellsPerChunk);
        int w = ix1 - ix0 + 1;

        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var indices = new System.Collections.Generic.List<int>();

        for (int iy = iy0; iy <= iy1; iy++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                vertices.Add(NodePoint(ix, iy));
                float slopeX = (hm.HeightAt(ix + 1, iy) - hm.HeightAt(ix - 1, iy)) / (2f * hm.StepMm);
                float slopeY = (hm.HeightAt(ix, iy + 1) - hm.HeightAt(ix, iy - 1)) / (2f * hm.StepMm);
                normals.Add(new Vector3(-slopeX, 1f, slopeY).Normalized());
            }

        for (int iy = iy0; iy < iy1; iy++)
            for (int ix = ix0; ix < ix1; ix++)
            {
                int near = (iy - iy0) * w + (ix - ix0), far = near + w;
                indices.Add(far); indices.Add(far + 1); indices.Add(near + 1);
                indices.Add(far); indices.Add(near + 1); indices.Add(near);
            }

        // Side walls, only where this chunk sits on the stock's outer edge.
        // left/right are as seen from outside the block.
        void Wall(Vector3 left, Vector3 right, Vector3 normal)
        {
            float bottom = hm.BottomZ * WorldScale.MmToM;
            int first = vertices.Count;
            vertices.Add(left); vertices.Add(right);
            vertices.Add(new Vector3(right.X, bottom, right.Z)); vertices.Add(new Vector3(left.X, bottom, left.Z));
            for (int i = 0; i < 4; i++)
                normals.Add(normal);
            indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
        }

        int lastX = hm.CountX - 1, lastY = hm.CountY - 1;
        for (int iy = iy0; iy < iy1; iy++)
        {
            if (ix0 == 0) Wall(NodePoint(0, iy + 1), NodePoint(0, iy), Vector3.Left);
            if (ix1 == lastX) Wall(NodePoint(lastX, iy), NodePoint(lastX, iy + 1), Vector3.Right);
        }
        for (int ix = ix0; ix < ix1; ix++)
        {
            if (iy0 == 0) Wall(NodePoint(ix, 0), NodePoint(ix + 1, 0), Vector3.Back);
            if (iy1 == lastY) Wall(NodePoint(ix + 1, lastY), NodePoint(ix, lastY), Vector3.Forward);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _chunkMeshes[cx + cy * _chunksX].Mesh = mesh;
    }
}
