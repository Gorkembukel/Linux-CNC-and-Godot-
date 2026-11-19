namespace CncTrainer.Bridge;

/// <summary>
/// Per-machine-type descriptor: which axes this machine actually has, and
/// which LinuxCNC joint each one is wired to (jog/home commands address
/// joints, not axis letters -- see BridgeClient.JogIncremental/HomeAxis).
/// Deliberately just data (no factory callbacks for a material model/visual
/// body yet): Phase A has exactly one real machine type, and adding factory
/// hooks with only one implementation to factor against would be
/// speculative. Built from a machine scene's own [Export] fields now (see
/// MachineSpec) rather than a hardcoded static instance -- that's what
/// lets the machine-scene-generator editor plugin parametrize a new
/// machine scene by just filling in a form.
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
    public readonly string IniFile;
    public readonly string DemoProgram;

    public MachineProfile(string name, (AxisId Axis, byte Joint)[] axes,
        string wslDistro, int statePort, int commandPort, double rateHz, string iniFile, string demoProgram)
    {
        Name = name;
        Axes = axes;
        WslDistro = wslDistro;
        StatePort = statePort;
        CommandPort = commandPort;
        RateHz = rateHz;
        IniFile = iniFile;
        DemoProgram = demoProgram;
    }
}
