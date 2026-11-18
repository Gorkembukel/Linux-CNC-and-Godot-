using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Triangles collected in managed arrays and handed to the engine in one
/// call: SurfaceTool crosses into the engine per vertex, which made a
/// lathe bar's mesh rebuild take tens of milliseconds. Normals as
/// SurfaceTool.GenerateNormals makes them: averaged over vertices at the
/// same position within a smooth group, per face in the Flat group.
/// </summary>
public sealed class MeshBuffer
{
    public const uint Flat = uint.MaxValue;

    private readonly List<Vector3> _vertices = new();
    private readonly List<uint> _groups = new();
    private readonly List<bool> _shown = new();
    private uint _group;

    /// <summary>Triangles added while this is set only shape the normals of
    /// the ones next to them; they are not in the mesh.</summary>
    public bool NormalsOnly { get; set; }

    public void SetSmoothGroup(uint group) => _group = group;

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        _groups.Add(_group);
        _shown.Add(!NormalsOnly);
    }

    private readonly record struct Corner(Vector3 P, uint Group)
    {
        public bool Equals(Corner other) => P == other.P && Group == other.Group;

        public override int GetHashCode() =>
            System.HashCode.Combine(System.BitConverter.SingleToInt32Bits(P.X), System.BitConverter.SingleToInt32Bits(P.Y),
                System.BitConverter.SingleToInt32Bits(P.Z), Group);
    }

    public ArrayMesh Commit()
    {
        int triangles = _groups.Count;
        var faceNormals = new Vector3[triangles];
        // Per vertex: its slot in sums (smooth groups), -1 in the Flat group.
        var slot = new int[_vertices.Count];
        var slots = new Dictionary<Corner, int>(_vertices.Count / 3);
        var sums = new List<Vector3>(_vertices.Count / 3);
        for (int t = 0; t < triangles; t++)
        {
            Vector3 a = _vertices[3 * t], b = _vertices[3 * t + 1], c = _vertices[3 * t + 2];
            // Godot's winding: the same normal Plane(a, b, c) gives.
            var n = (a - c).Cross(a - b);
            faceNormals[t] = n;
            for (int k = 0; k < 3; k++)
            {
                if (_groups[t] == Flat)
                {
                    slot[3 * t + k] = -1;
                    continue;
                }
                ref int s = ref CollectionsMarshal.GetValueRefOrAddDefault(slots, new Corner(_vertices[3 * t + k], _groups[t]), out bool seen);
                if (!seen)
                {
                    s = sums.Count;
                    sums.Add(Vector3.Zero);
                }
                sums[s] += n;
                slot[3 * t + k] = s;
            }
        }

        var vertices = new List<Vector3>(_vertices.Count);
        var normals = new List<Vector3>(_vertices.Count);
        for (int t = 0; t < triangles; t++)
        {
            if (!_shown[t])
                continue;
            for (int k = 0; k < 3; k++)
            {
                int s = slot[3 * t + k];
                vertices.Add(_vertices[3 * t + k]);
                normals.Add((s < 0 ? faceNormals[t] : sums[s]).Normalized());
            }
        }

        var mesh = new ArrayMesh();
        if (vertices.Count == 0)
            return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
