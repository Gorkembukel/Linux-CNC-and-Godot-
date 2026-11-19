using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Spec;

/// <summary>
/// Forward kinematics of a mill built from an axis list. Axes carried by the
/// tool side move the tool by +value, axes carried by the workpiece side move
/// the workpiece by -value (a LinuxCNC coordinate describes the tool relative
/// to the work). List order is chain order, first = closest to the base.
///
/// Everything is in the machine's local Godot frame, metres: X = machine X,
/// Y (up) = machine Z, Z = -machine Y; origin = work zero. Each link's
/// transform is a displacement from the all-axes-at-0 pose, so nesting one
/// Node3D per link with these as local transforms reproduces the chain.
/// </summary>
public sealed class MachineKinematics
{
    public readonly AxisDef[] ToolChain;
    public readonly AxisDef[] WorkChain;

    public MachineKinematics(IEnumerable<AxisDef> axes)
    {
        ToolChain = axes.Where(a => !a.OnWorkpiece).ToArray();
        WorkChain = axes.Where(a => a.OnWorkpiece).ToArray();
    }

    public static Vector3 MachineToLocal(Vector3 machineMm) =>
        new Vector3(machineMm.X, machineMm.Z, -machineMm.Y) * WorldScale.MmToM;

    public static Vector3 Direction(AxisId letter) => letter switch
    {
        AxisId.X or AxisId.A => Vector3.Right,
        AxisId.Y or AxisId.B => Vector3.Forward,
        _ => Vector3.Up,
    };

    public static Transform3D LinkTransform(AxisDef axis, float value)
    {
        value -= axis.ControlOffset;
        float signed = axis.OnWorkpiece ? -value : value;
        if (!axis.IsRotary)
            return new Transform3D(Basis.Identity, Direction(axis.Letter) * signed * WorldScale.MmToM);

        var pivot = MachineToLocal(axis.PivotMm);
        var rotation = new Basis(Direction(axis.Letter), Mathf.DegToRad(signed));
        return new Transform3D(rotation, pivot - rotation * pivot);
    }

    public Transform3D ToolTransform(AxisVector position) => Chain(ToolChain, position);
    public Transform3D WorkTransform(AxisVector position) => Chain(WorkChain, position);

    /// <summary>Tool pose seen from the workpiece: origin = tool tip,
    /// basis Y = tool axis pointing from the tip toward the spindle.</summary>
    public Transform3D ToolInWork(AxisVector position) =>
        WorkTransform(position).AffineInverse() * ToolTransform(position);

    private static Transform3D Chain(AxisDef[] chain, AxisVector position)
    {
        var t = Transform3D.Identity;
        foreach (var axis in chain)
            t *= LinkTransform(axis, position.Get(axis.Letter));
        return t;
    }
}
