using Godot;

namespace CncTrainer.Bridge;

/// <summary>
/// The latest CONFIRMED machine state from the bridge. Nothing here is predicted --
/// see PredictedState for the render-time extrapolation layer (Phase 1). Plain C#
/// class, not a GodotObject: it holds no native resources and doesn't need the
/// engine lifecycle (a GodotObject here just leaks an ObjectDB entry since nothing
/// ever calls Free()/Dispose() on it).
/// </summary>
public class MachineState
{
    public Vector2 Position; // X, Z in mm, machine coordinates
    public Vector2 Velocity;
    public double FeedRate;
    public double SpindleRpm;
    public float FeedOverride = 1.0f;
    public float SpindleOverride = 1.0f;
    public int ToolNumber;
    public float ToolNoseRadius;
    public bool SpindleEnabled;
    public TaskMode TaskMode;
    public InterpState InterpState;
    public byte ExecState;
    public int MotionLine;
    public bool Estop;
    public bool MachineOn;
    public bool HomedX, HomedZ;
    public bool InCycle;

    public ulong LastSeq;
    public long LastTMonoNs;
    public ulong SampleCount;

    public void ApplyState(PacketHeader header, StateSample s)
    {
        if (SampleCount > 0 && header.Seq <= LastSeq)
            return; // stale/reordered packet, ignore

        Position = new Vector2((float)s.PosX, (float)s.PosZ);
        Velocity = new Vector2((float)s.VelX, (float)s.VelZ);
        FeedRate = s.FeedRate;
        SpindleRpm = s.SpindleRpm;
        FeedOverride = s.FeedOverride;
        SpindleOverride = s.SpindleOverride;
        ToolNumber = s.ToolNumber;
        ToolNoseRadius = s.ToolNoseRadius;
        SpindleEnabled = s.SpindleEnabled;
        TaskMode = (TaskMode)s.TaskModeRaw;
        InterpState = (InterpState)s.InterpStateRaw;
        ExecState = s.ExecState;
        MotionLine = s.MotionLine;
        Estop = s.Estop;
        MachineOn = s.MachineOn;
        HomedX = s.HomedX;
        HomedZ = s.HomedZ;
        InCycle = s.InCycle;

        LastSeq = header.Seq;
        LastTMonoNs = header.TMonoNs;
        SampleCount++;
    }
}
