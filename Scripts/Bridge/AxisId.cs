namespace CncTrainer.Bridge;

/// <summary>The 6 conventional CNC axis letters (LinuxCNC's own convention),
/// fixed capacity rather than a variable-length axis list -- see
/// AxisVector.cs and BridgeProtocol.cs's StateSample for why.</summary>
public enum AxisId { X = 0, Y = 1, Z = 2, A = 3, B = 4, C = 5 }
