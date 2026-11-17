using CncTrainer.Bridge;

namespace CncTrainer;

public interface IMachine
{
    MachineRig Rig { get; }
    Scheduling.JobScheduler Scheduler { get; }
    void StopBackingProcess();
}
