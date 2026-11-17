namespace CncTrainer.Bridge;

/// <summary>The 6 conventional CNC axis letters (LinuxCNC's own convention),
/// fixed capacity rather than a variable-length axis list -- see
/// AxisVector.cs and BridgeProtocol.cs's StateSample for why.</summary>
public enum AxisId
{
    /// <summary>Linear X (mm). Lathe: the radius, away from the spindle axis.</summary>
    X = 0,
    /// <summary>Linear Y (mm), mills only.</summary>
    Y = 1,
    /// <summary>Linear Z (mm): along the spindle axis.</summary>
    Z = 2,
    /// <summary>Rotary about X (degrees).</summary>
    A = 3,
    /// <summary>Rotary about Y (degrees).</summary>
    B = 4,
    /// <summary>Rotary about Z (degrees).</summary>
    C = 5,
}
