using System.Collections.Generic;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Material;

namespace CncTrainer;

/// <summary>
/// Draws a VoxelStock as chunked surface-nets meshes so a cut only rebuilds
/// the chunks it touched. This node's frame is the stock's own (see
/// VoxelStock), scaled to metres.
/// </summary>
[Tool]
public partial class VoxelStockVisual : Node3D
{
    private const int ChunkNodes = 16;
    public int MaxChunkRebuildsPerCall = 6;

    public VoxelStock Stock { get; private set; }

    private int _chunksX, _chunksY, _chunksZ;
    private MeshInstance3D[] _chunkMeshes;
    private bool[] _chunkDirty;
    private readonly List<Vector3> _vertices = new(), _normals = new();
    private readonly List<int> _indices = new();

    /// <summary>What a gauge reads at a point (global) over the stock: the
    /// depth gauge the depth below the stock's top face, the caliper the
    /// sizes along the stock's X and Y -- the pocket or slot the point is
    /// in, or the outside of the part when the point is on its top face.</summary>
    public string Reading(HandToolKind gauge, Vector3 globalPoint)
    {
        if (Stock == null || gauge is not (HandToolKind.Caliper or HandToolKind.DepthGauge))
            return null;
        var p = ToLocal(globalPoint) / WorldScale.MmToM;
        if (Stock.SurfaceY(p.X, p.Z) is not float surface)
            return "Burada ölçülecek malzeme yok";
        float depth = Mathf.Max(0f, -surface);
        if (gauge == HandToolKind.DepthGauge)
            return System.FormattableString.Invariant($"Derinlik mastarı: {depth:0.00} mm  (parçanın üst yüzünden)");

        bool inPocket = depth > 0.1f;
        var at = new Vector3(p.X, inPocket ? surface + 0.3f : surface - 0.5f, p.Z);
        static string Size(float? mm) => mm is float value ? System.FormattableString.Invariant($"{Mathf.Round(value / 0.02f) * 0.02f:0.00} mm") : "açık";
        return $"Kumpas ({(inPocket ? "iç ölçü" : "dış ölçü")}): X {Size(Stock.Span(at, Vector3.Right))}, Y {Size(Stock.Span(at, Vector3.Back))}";
    }

    public void Build(VoxelStock stock)
    {
        Stock = stock;
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.79f, 0.82f),
            Metallic = 0.6f,
            Roughness = 0.4f,
        };

        _chunksX = Mathf.CeilToInt(stock.CountX / (float)ChunkNodes);
        _chunksY = Mathf.CeilToInt(stock.CountY / (float)ChunkNodes);
        _chunksZ = Mathf.CeilToInt(stock.CountZ / (float)ChunkNodes);
        _chunkMeshes = new MeshInstance3D[_chunksX * _chunksY * _chunksZ];
        _chunkDirty = new bool[_chunkMeshes.Length];
        for (int c = 0; c < _chunkMeshes.Length; c++)
        {
            _chunkMeshes[c] = new MeshInstance3D { MaterialOverride = material };
            AddChild(_chunkMeshes[c]);
            _chunkDirty[c] = true;
        }
        RebuildDirtyChunks(_chunkMeshes.Length);
    }

    public void MarkDirty(VoxelCutResult r)
    {
        // A chunk's mesh also reads the layer of nodes on each side of it.
        int cx0 = Mathf.Max(0, (r.MinIx - 1) / ChunkNodes), cx1 = Mathf.Min(_chunksX - 1, (r.MaxIx + 1) / ChunkNodes);
        int cy0 = Mathf.Max(0, (r.MinIy - 1) / ChunkNodes), cy1 = Mathf.Min(_chunksY - 1, (r.MaxIy + 1) / ChunkNodes);
        int cz0 = Mathf.Max(0, (r.MinIz - 1) / ChunkNodes), cz1 = Mathf.Min(_chunksZ - 1, (r.MaxIz + 1) / ChunkNodes);
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    _chunkDirty[(cz * _chunksY + cy) * _chunksX + cx] = true;
    }

    public void RebuildDirtyChunks(int budgetOverride = -1)
    {
        int budget = budgetOverride >= 0 ? budgetOverride : MaxChunkRebuildsPerCall;
        for (int c = 0; c < _chunkDirty.Length && budget > 0; c++)
        {
            if (!_chunkDirty[c])
                continue;
            RebuildChunk(c);
            _chunkDirty[c] = false;
            budget--;
        }
    }

    private void RebuildChunk(int c)
    {
        int cx = c % _chunksX, cy = c / _chunksX % _chunksY, cz = c / (_chunksX * _chunksY);
        _vertices.Clear();
        _normals.Clear();
        _indices.Clear();
        VoxelMesher.Build(Stock, cx * ChunkNodes, cy * ChunkNodes, cz * ChunkNodes,
            (cx + 1) * ChunkNodes, (cy + 1) * ChunkNodes, (cz + 1) * ChunkNodes, _vertices, _normals, _indices);
        if (_indices.Count == 0)
        {
            _chunkMeshes[c].Mesh = null;
            return;
        }

        var vertices = new Vector3[_vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = _vertices[i] * WorldScale.MmToM;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _chunkMeshes[c].Mesh = mesh;
    }
}
