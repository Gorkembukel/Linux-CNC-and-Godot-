using CncTrainer.Bridge;

namespace CncTrainer;

public interface IMachine
{
    MachineRig Rig { get; }
    void StopBackingProcess();
}
