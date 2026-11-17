using Godot;
using CncTrainer.Spec;

namespace CncTrainer.Machining;

/// <summary>
/// A tool as it sits in the machine: what it is, the state it is in, and how
/// far its tip really is from where the control assumes it (mm): X = radial
/// on a lathe, Y = longer than nominal on a mill, Z = along the lathe's Z.
/// </summary>
public readonly record struct MountedTool(ToolSpec Spec, ToolState State, Vector3 ErrorMm)
{
    public bool Present => Spec != null;
    public bool Cuts => Spec != null && !State.Broken;
}

/// <summary>Answers "what is really cutting when the control says tool N".</summary>
public interface IToolSource
{
    MountedTool Active(int controlToolNumber);
}

/// <summary>Tools that are simply there: every tool of the spec, exactly
/// as specified, in the pocket the control expects. Machines without
/// hands-on setup use this.</summary>
public sealed class SpecTools : IToolSource
{
    private readonly MachineSpec _spec;
    private readonly ToolCrib _crib = new();

    public SpecTools(MachineSpec spec) => _spec = spec;

    public MountedTool Active(int controlToolNumber) =>
        _spec.FindTool(controlToolNumber) is ToolSpec tool ? new MountedTool(tool, _crib[controlToolNumber], Vector3.Zero) : default;
}
