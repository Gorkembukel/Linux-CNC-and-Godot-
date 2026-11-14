namespace CncTrainer.Bridge;

/// <summary>
/// Per-machine-type descriptor: which axes this machine actually has, and
/// which LinuxCNC joint each one is wired to (jog/home commands address
/// joints, not axis letters -- see BridgeClient.JogIncremental/HomeAxis).
/// Deliberately just data (no factory callbacks for a material model/visual
/// body yet): Phase A has exactly one real machine type, and adding factory
/// hooks with only one implementation to factor against would be
/// speculative. Phase B's mill adds its own MachineProfile instance; the
/// list this lives in (see Main.cs's `_rigs`) is the actual Open/Closed
/// extension point -- adding a machine means appending to that list, not
/// editing this class.
/// </summary>
public sealed class MachineProfile
{
    public readonly string Name;
    public readonly (AxisId Axis, byte Joint)[] Axes;

    public MachineProfile(string name, (AxisId Axis, byte Joint)[] axes)
    {
        Name = name;
        Axes = axes;
    }

    public static readonly MachineProfile Lathe = new("Lathe", new[]
    {
        (AxisId.X, (byte)0),
        (AxisId.Z, (byte)1),
    });
}
