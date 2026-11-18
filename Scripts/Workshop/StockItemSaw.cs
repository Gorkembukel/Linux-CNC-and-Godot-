using Godot;
using CncTrainer.Chips;
using CncTrainer.Interaction;

namespace CncTrainer.Workshop;

/// <summary>Sawing a piece of stock. Held in a vise, chuck or fixture the
/// holder decides (jaws, spindle); loose, it is sawn where it lies unless
/// the project setting asks for clamping (WorkshopSettings).</summary>
public partial class StockItem : ISawable
{
    private const float M = WorldScale.MmToM;

    public MaterialKind SawMaterial => Blank.Material;

    private ISawable Holder => SawnBy ?? GetParent() as ISawable;

    public SawCut? PlanCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        why = null;
        if (Fixed)
            return Holder?.PlanCut(globalPoint, angleDeg, out why);
        if (WorkshopSettings.SawRequiresClamping)
        {
            why = "Önce parçayı bağla (mengene, ayna ya da bağlama)";
            return null;
        }
        return PlanOwnCut(globalPoint, angleDeg, out why);
    }

    /// <summary>The cut through this piece at the point, whatever holds it:
    /// square cuts on raw or turned stock exactly, the rest through voxels.</summary>
    public SawCut? PlanOwnCut(Vector3 globalPoint, float angleDeg, out string why)
    {
        why = null;
        float x = ToLocal(globalPoint).X / M, half = Blank.LengthMm / 2f;
        SawCut? plan;
        if (angleDeg == 0f && Machined == null && SectionAt(x) is { } section)
        {
            var basis = GlobalBasis.Orthonormalized();
            plan = new SawCut(ToGlobal(new Vector3(x * M, 0f, 0f)), basis.X, -basis.Y, section.WidthMm * M, section.HeightMm * M, section.AreaMm2, x);
        }
        else
            plan = VoxelSaw.Plan(Voxels(), GlobalTransform * VoxelFrame, globalPoint,
                VoxelSaw.Normal(GlobalBasis.Orthonormalized().X, angleDeg), angleDeg, x);
        if (plan is not { } cut)
            return null;
        float reach = AngledReachMm(cut);
        if (x - reach < -half + 2f || x + reach > half - 2f)
        {
            why = "Uca çok yakın";
            return null;
        }
        return cut;
    }

    /// <summary>How far along the stock an angled cut reaches either side of where it is aimed (mm).</summary>
    public static float AngledReachMm(SawCut cut) => cut.WidthM / M / 2f * Mathf.Abs(Mathf.Sin(Mathf.DegToRad(cut.AngleDeg)));

    public void FinishCut(SawCut cut)
    {
        if (Fixed)
        {
            Holder?.FinishCut(cut);
            return;
        }
        var scene = GetTree().CurrentScene;
        if (cut.AngleDeg == 0f && Machined == null)
        {
            var whole = GlobalTransform;
            var (near, nearAt, far, farAt) = SplitAt(cut.AtMm, BenchVise.KerfMm);
            foreach (var (piece, at) in new[] { (near, nearAt), (far, farAt) })
                if (piece != null)
                {
                    scene.AddChild(piece);
                    piece.GlobalTransform = whole * new Transform3D(Basis.Identity, new Vector3(at * M, 0f, 0f));
                }
        }
        else
        {
            var voxels = Voxels().Copy();
            var voxelGlobal = GlobalTransform * VoxelFrame;
            var (dropped, _) = VoxelSaw.Split(voxels, voxelGlobal, cut, null);
            VoxelSaw.Spawn(voxels, voxelGlobal, Blank.Material, scene);
            VoxelSaw.Spawn(dropped, voxelGlobal, Blank.Material, scene);
        }
        QueueFree();
    }
}

/// <summary>Workshop rules that are a choice, not physics: Project Settings,
/// section cnc_trainer/workshop.</summary>
public static class WorkshopSettings
{
    public const string SawRequiresClampingKey = "cnc_trainer/workshop/saw_requires_clamping";

    /// <summary>Only stock held in a vise, chuck or fixture can be sawn (off: loose stock too).</summary>
    public static bool SawRequiresClamping =>
        ProjectSettings.HasSetting(SawRequiresClampingKey) && ProjectSettings.GetSetting(SawRequiresClampingKey).AsBool();
}
