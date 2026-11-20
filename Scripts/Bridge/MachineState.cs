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
    public AxisVector Position; // machine coordinates, mm/deg depending on axis
    public AxisVector Velocity;
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
    public byte ActiveAxisMask, HomedMask;
    public bool InCycle;
    public bool ToolChangePending;
    /// <summary>Active work offset (G5x + G92 + tool offset) per axis.</summary>
    public AxisVector WorkOffset;
    /// <summary>Position in the active work coordinate system, what a
    /// program's X/Y/Z words refer to.</summary>
    public AxisVector WorkPosition => Position - WorkOffset;
    /// <summary>The active work system's own offset (G54 = index 1).</summary>
    public AxisVector G5xOffset;
    public int G5xIndex = 1;

    /// <summary>The move in progress is a rapid traverse (G0).</summary>
    public bool Rapid;
    /// <summary>Flood (M8) or mist (M7) coolant is on.</summary>
    public bool Coolant;

    public ulong LastSeq;
    public long LastTMonoNs;
    public ulong SampleCount;

    public bool IsHomed(AxisId axis) => (HomedMask & (1 << (int)axis)) != 0;
    public bool HasAxis(AxisId axis) => (ActiveAxisMask & (1 << (int)axis)) != 0;

    public void ApplyState(PacketHeader header, StateSample s)
    {
        if (SampleCount > 0 && header.Seq <= LastSeq)
            return; // stale/reordered packet, ignore

        Position = s.Position;
        Velocity = s.Velocity;
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
        ActiveAxisMask = s.ActiveAxisMask;
        HomedMask = s.HomedMask;
        InCycle = s.InCycle;
        ToolChangePending = s.ToolChangePending;
        WorkOffset = s.WorkOffset;
        G5xOffset = s.G5xOffset;
        G5xIndex = s.G5xIndex;
        Rapid = s.MotionTypeRaw == 1;
        Coolant = s.Coolant != 0;

        LastSeq = header.Seq;
        LastTMonoNs = header.TMonoNs;
        SampleCount++;
    }
}
