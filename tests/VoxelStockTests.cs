using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class VoxelStockTests
{
    private static Transform3D Upright(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));
    private static readonly CutterShape FlatEndMill = new(RadiusMm: 3f, CornerRadiusMm: 0f, LengthMm: 30f, TipAngleDeg: 0f);

    [Fact]
    public void Slot_RemovesTheAnalyticVolume()
    {
        var stock = VoxelStock.Block(60f, 20f, 40f, stepMm: 0.5f);
        var first = stock.Cut(Upright(-20f, 10f, 0f), Upright(-20f, -4f, 0f), FlatEndMill);
        var second = stock.Cut(Upright(-20f, -4f, 0f), Upright(20f, -4f, 0f), FlatEndMill);

        float expected = (40f * 6f + Mathf.Pi * 9f) * 4f;
        Assert.InRange(first.VolumeMm3 + second.VolumeMm3, expected * 0.97f, expected * 1.03f);
        Assert.False(stock.IsMaterialAt(new Vector3(0f, -2f, 0f)));
        Assert.False(stock.IsMaterialAt(new Vector3(0f, -2f, 2.5f)));
        Assert.True(stock.IsMaterialAt(new Vector3(0f, -2f, 3.5f)));
        Assert.True(stock.IsMaterialAt(new Vector3(0f, -4.5f, 0f)));
    }

    [Fact]
    public void BallNose_LeavesARoundedFloor()
    {
        var stock = VoxelStock.Block(40f, 20f, 40f, stepMm: 0.5f);
        var ball = new CutterShape(RadiusMm: 5f, CornerRadiusMm: 5f, LengthMm: 30f, TipAngleDeg: 0f);
        stock.Cut(Upright(0f, 10f, 0f), Upright(0f, -5f, 0f), ball);

        Assert.False(stock.IsMaterialAt(new Vector3(0f, -4.5f, 0f)));
        Assert.True(stock.IsMaterialAt(new Vector3(4f, -4.5f, 0f)));
        Assert.False(stock.IsMaterialAt(new Vector3(4f, -1f, 0f)));
    }

    [Fact]
    public void Drill_CutsAConicalPoint()
    {
        var stock = VoxelStock.Block(40f, 20f, 40f, stepMm: 0.5f);
        var drill = new CutterShape(RadiusMm: 4f, CornerRadiusMm: 0f, LengthMm: 40f, TipAngleDeg: 90f);
        stock.Cut(Upright(0f, 10f, 0f), Upright(0f, -10f, 0f), drill);

        Assert.False(stock.IsMaterialAt(new Vector3(0f, -9f, 0f)));
        Assert.True(stock.IsMaterialAt(new Vector3(3f, -9f, 0f)));
        Assert.False(stock.IsMaterialAt(new Vector3(3f, -5f, 0f)));
    }

    [Fact]
    public void TiltedTool_CutsIntoTheSideFace()
    {
        var stock = VoxelStock.Block(40f, 20f, 40f, stepMm: 0.5f);
        // Tool axis along +Z: the tip enters through the block's +Z face.
        var sideways = new Basis(Vector3.Right, Mathf.Pi / 2f);
        var result = stock.Cut(new Transform3D(sideways, new Vector3(0f, -10f, 30f)),
            new Transform3D(sideways, new Vector3(0f, -10f, 15f)), FlatEndMill);

        Assert.True(result.MaterialRemoved);
        Assert.InRange(result.VolumeMm3, Mathf.Pi * 9f * 5f * 0.95f, Mathf.Pi * 9f * 5f * 1.05f);
        Assert.False(stock.IsMaterialAt(new Vector3(0f, -10f, 17f)));
        Assert.True(stock.IsMaterialAt(new Vector3(0f, -10f, 14f)));
        Assert.True(stock.IsMaterialAt(new Vector3(0f, -5f, 17f)));
    }

    [Fact]
    public void Cylinder_HasMaterialOnlyInsideItsRadius()
    {
        var stock = VoxelStock.CylinderAlongX(80f, 40f, stepMm: 1f);
        Assert.True(stock.IsMaterialAt(new Vector3(0f, -20f, 0f)));
        Assert.True(stock.IsMaterialAt(new Vector3(30f, -2f, 0f)));
        Assert.False(stock.IsMaterialAt(new Vector3(0f, -3f, 18f)));
    }

    [Fact]
    public void Mesh_IsClosedAndFacesOutward_AcrossAChunkSeam()
    {
        var stock = VoxelStock.Block(10f, 6f, 8f, stepMm: 0.5f);
        stock.Cut(Upright(0f, 5f, 0f), Upright(0f, -3f, 0f), new CutterShape(2f, 0f, 20f, 0f));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        VoxelMesher.Build(stock, 0, 0, 0, 11, stock.CountY, stock.CountZ, vertices, normals, indices);
        int firstChunkIndices = indices.Count;
        VoxelMesher.Build(stock, 11, 0, 0, stock.CountX, stock.CountY, stock.CountZ, vertices, normals, indices);
        Assert.True(firstChunkIndices > 0 && indices.Count > firstChunkIndices);

        int bottomTriangles = 0;
        float volume = 0f;
        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            volume += a.Dot(c.Cross(b)) / 6f;
            if (a.Y > -5.9f || b.Y > -5.9f || c.Y > -5.9f)
                continue;
            bottomTriangles++;
            Assert.True((c - a).Cross(b - a).Y < 0f, "bottom face must be clockwise seen from below");
            Assert.True(normals[indices[i]].Y < -0.5f);
        }
        Assert.True(bottomTriangles > 0);

        // Surface nets bevel every sharp edge by about one step, so the mesh
        // encloses slightly less than the exact solid.
        float expected = 10f * 6f * 8f - Mathf.Pi * 4f * 3f;
        Assert.InRange(volume, expected * 0.90f, expected);
    }
}
