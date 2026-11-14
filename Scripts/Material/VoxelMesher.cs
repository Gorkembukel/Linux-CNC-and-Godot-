using System.Collections.Generic;
using Godot;

namespace CncTrainer.Material;

/// <summary>
/// Surface-nets mesh of a VoxelStock region: one vertex per grid cell the
/// surface passes through (the average of its edge crossings), one quad per
/// grid edge that crosses the surface. Vertices are in the stock's mm frame.
/// </summary>
public static class VoxelMesher
{
    private static readonly (int A, int B)[] CellEdges =
    {
        (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7),
    };

    /// <summary>Builds the quads owned by the grid edges starting at nodes
    /// [x0, x1) x [y0, y1) x [z0, z1). Triangles are wound clockwise seen
    /// from outside the material (Godot's front face).</summary>
    public static void Build(VoxelStock stock, int x0, int y0, int z0, int x1, int y1, int z1,
        List<Vector3> vertices, List<Vector3> normals, List<int> indices)
    {
        x1 = Mathf.Min(x1, stock.CountX - 1); y1 = Mathf.Min(y1, stock.CountY - 1); z1 = Mathf.Min(z1, stock.CountZ - 1);
        int cx0 = Mathf.Max(0, x0 - 1), cy0 = Mathf.Max(0, y0 - 1), cz0 = Mathf.Max(0, z0 - 1);
        int sx = x1 - cx0, sy = y1 - cy0, sz = z1 - cz0;
        if (sx <= 0 || sy <= 0 || sz <= 0)
            return;

        var cellVertex = new int[sx * sy * sz];
        var corner = new float[8];
        for (int cz = 0; cz < sz; cz++)
            for (int cy = 0; cy < sy; cy++)
                for (int cx = 0; cx < sx; cx++)
                {
                    int gx = cx0 + cx, gy = cy0 + cy, gz = cz0 + cz;
                    int inside = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        corner[c] = stock.DistanceAt(gx + (c & 1), gy + ((c >> 1) & 1), gz + (c >> 2));
                        if (corner[c] > 0f)
                            inside++;
                    }
                    if (inside == 0 || inside == 8)
                    {
                        cellVertex[(cz * sy + cy) * sx + cx] = -1;
                        continue;
                    }

                    var sum = Vector3.Zero;
                    int crossings = 0;
                    foreach (var (a, b) in CellEdges)
                    {
                        float da = corner[a], db = corner[b];
                        if ((da > 0f) == (db > 0f))
                            continue;
                        float t = da / (da - db);
                        var pa = new Vector3(a & 1, (a >> 1) & 1, a >> 2);
                        var pb = new Vector3(b & 1, (b >> 1) & 1, b >> 2);
                        sum += pa + (pb - pa) * t;
                        crossings++;
                    }
                    var local = sum / crossings;
                    var gradient = new Vector3(
                        corner[1] - corner[0] + corner[3] - corner[2] + corner[5] - corner[4] + corner[7] - corner[6],
                        corner[2] - corner[0] + corner[3] - corner[1] + corner[6] - corner[4] + corner[7] - corner[5],
                        corner[4] - corner[0] + corner[5] - corner[1] + corner[6] - corner[2] + corner[7] - corner[3]);

                    cellVertex[(cz * sy + cy) * sx + cx] = vertices.Count;
                    vertices.Add(stock.NodePosition(gx, gy, gz) + local * stock.StepMm);
                    normals.Add(gradient.LengthSquared() > 1e-12f ? -gradient.Normalized() : Vector3.Up);
                }

        int Cell(int gx, int gy, int gz) => cellVertex[((gz - cz0) * sy + (gy - cy0)) * sx + (gx - cx0)];

        void Quad(int a, int b, int c, int d, bool flip)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0)
                return;
            if (flip)
                (b, d) = (d, b);
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(a); indices.Add(c); indices.Add(d);
        }

        for (int z = z0; z < z1; z++)
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    bool here = stock.DistanceAt(x, y, z) > 0f;
                    if (y > 0 && z > 0 && here != stock.DistanceAt(x + 1, y, z) > 0f)
                        Quad(Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), here);
                    if (x > 0 && z > 0 && here != stock.DistanceAt(x, y + 1, z) > 0f)
                        Quad(Cell(x - 1, y, z - 1), Cell(x - 1, y, z), Cell(x, y, z), Cell(x, y, z - 1), here);
                    if (x > 0 && y > 0 && here != stock.DistanceAt(x, y, z + 1) > 0f)
                        Quad(Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z), here);
                }
    }
}
