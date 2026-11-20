using System.Collections.Generic;

namespace CncTrainer.Spec;

/// <summary>What a machine's control keeps through a power cycle: its tool
/// table (as LinuxCNC holds it, wear included) and the active work offset
/// (index 1 = G54; offsets in axis order X Y Z A B C).</summary>
public sealed record ControlMemory(int G5xIndex, float[] G5x, IReadOnlyDictionary<int, (float X, float Z)> Tools);
