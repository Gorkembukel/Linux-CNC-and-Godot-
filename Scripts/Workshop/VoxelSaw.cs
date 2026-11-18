using Godot;
using CncTrainer.Interaction;
using CncTrainer.Material;

namespace CncTrainer.Workshop;

/// <summary>Saw cuts through stock held as voxels (milled stock, or any
/// stock cut at an angle): where the plane meets the material, and the split.
/// voxelGlobal is the voxel grid's frame in the world, in metres (a
/// VoxelStockVisual's global transform); the grid itself is in mm.</summary>
public static class VoxelSaw
{
    private const float M = WorldScale.MmToM;

    /// <summary>A cut square to `axis`, turned angleDeg about the vertical.</summary>
    public static Vector3 Normal(Vector3 axis, float angleDeg) =>
        (new Basis(Vector3.Up, Mathf.DegToRad(angleDeg)) * axis).Normalized();

    /// <summary>The saw goes in from above: straight down, as far as the plane allows.</summary>
    public static Vector3 Down(Vector3 normal) => (Vector3.Down - normal * normal.Dot(Vector3.Down)).Normalized();

    public static SawCut? Plan(VoxelStock voxels, Transform3D voxelGlobal, Vector3 globalPoint, Vector3 normal, float angleDeg, float atMm)
    {
        var toGrid = voxelGlobal.Basis.Orthonormalized().Inverse();
        var down = Down(normal);
        if (voxels.PlaneSection(voxelGlobal.AffineInverse() * globalPoint / M, toGrid * normal, toGrid * down) is not { } section)
            return null;
        return new SawCut(voxelGlobal * (section.CentreMm * M), normal, down, section.WidthMm * M, section.DepthMm * M, section.AreaMm2, atMm, angleDeg);
    }

    /// <summary>Splits along the cut: `voxels` keeps the side keepGlobal is
    /// on (the bigger side when null), changed in place; the other side
    /// comes back as its own grid.</summary>
    public static (VoxelStock Dropped, VoxelCutResult Changed) Split(VoxelStock voxels, Transform3D voxelGlobal, SawCut cut, Vector3? keepGlobal)
    {
        var toGrid = voxelGlobal.Basis.Orthonormalized().Inverse();
        var at = voxelGlobal.AffineInverse() * cut.Center / M;
        var normal = (toGrid * cut.Normal).Normalized();
        bool keepPositive;
        if (keepGlobal is Vector3 keep)
            keepPositive = normal.Dot(voxelGlobal.AffineInverse() * keep / M - at) > 0f;
        else
        {
            var negative = voxels.Copy();
            negative.RemoveBeyond(at, normal, 0f);
            keepPositive = negative.VolumeMm3() < voxels.VolumeMm3() / 2f;
        }
        var towardDropped = keepPositive ? -normal : normal;
        var dropped = voxels.Copy();
        var changed = voxels.RemoveBeyond(at, towardDropped, BenchVise.KerfMm / 2f);
        dropped.RemoveBeyond(at, -towardDropped, BenchVise.KerfMm / 2f);
        return (dropped, changed);
    }

    /// <summary>A loose piece made of these voxels, put where they are
    /// (voxelGlobal) in the scene; null when there is no material.</summary>
    public static StockItem Spawn(VoxelStock voxels, Transform3D voxelGlobal, Chips.MaterialKind material, Node scene)
    {
        var piece = StockItem.FromVoxels(voxels, material, out var centreMm);
        if (piece == null)
            return null;
        scene.AddChild(piece);
        piece.GlobalTransform = voxelGlobal * new Transform3D(Basis.Identity, centreMm * M);
        piece.Burred = true;
        return piece;
    }
}
