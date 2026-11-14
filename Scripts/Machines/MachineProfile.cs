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

    // Launch metadata for MachineProcessController: which WSL distro this
    // machine's LinuxCNC instance runs in (Phase A/B's isolation work --
    // each machine needs a genuinely separate distro, not just different
    // ports, see PLAN.md) and the bridge/main.py args that must agree with
    // BridgeClient's own port Export defaults for this machine's rig.
    public readonly string WslDistro;
    public readonly int StatePort;
    public readonly int CommandPort;
    public readonly double RateHz;

    public MachineProfile(string name, (AxisId Axis, byte Joint)[] axes,
        string wslDistro, int statePort, int commandPort, double rateHz)
    {
        Name = name;
        Axes = axes;
        WslDistro = wslDistro;
        StatePort = statePort;
        CommandPort = commandPort;
        RateHz = rateHz;
    }

    public static readonly MachineProfile Lathe = new("Lathe",
        new[] { (AxisId.X, (byte)0), (AxisId.Z, (byte)1) },
        wslDistro: "Ubuntu", statePort: 9200, commandPort: 9201, rateHz: 100.0);
}
