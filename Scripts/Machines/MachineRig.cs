namespace CncTrainer.Bridge;

/// <summary>
/// Bundles one live machine's bridge connection with its axis profile.
/// Main.cs holds a list of these -- today exactly one (the lathe); Phase
/// B's mill adds a second to the same list without changing this class.
/// </summary>
public sealed class MachineRig
{
    public readonly MachineProfile Profile;
    public readonly BridgeClient Bridge;

    public MachineRig(MachineProfile profile, BridgeClient bridge)
    {
        Profile = profile;
        Bridge = bridge;
    }
}
