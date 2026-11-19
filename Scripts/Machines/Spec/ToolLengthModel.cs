namespace CncTrainer.Spec;

/// <summary>Which point of the spindle the control's Z refers to when no
/// tool length offset is active.</summary>
public enum ToolLengthReference
{
    /// <summary>Simplified: Z is the tip of a tool of catalogue length. Only
    /// each tool's setting error (a few mm) goes in the tool table, and
    /// forgetting G43 costs just that error.</summary>
    NominalToolTip,
    /// <summary>Real machine: Z is the spindle gauge line. The whole tool
    /// length goes in the tool table, and without G43 the tip dives that
    /// full length below the programmed Z.</summary>
    SpindleGaugeLine,
}

/// <summary>
/// Where a mill tool's tip and the spindle nose sit relative to the point the
/// kinematics place at the commanded position (the "reference point"), and
/// what the tool table holds. Engine-free.
///
/// Lengths are gauge lengths: spindle nose to tip, so a tool's catalogue
/// length (out of the holder) plus the holder. NominalToolTip puts the
/// reference point at a nominal tool's tip. SpindleGaugeLine keeps the
/// spindle at a fixed height above it (the longest tool's gauge length, so
/// the reach of the machine stays what its axis limits describe) and shifts
/// the control's Z by the same amount, so LinuxCNC's Z is the gauge line.
/// </summary>
public readonly record struct ToolLengthModel(ToolLengthReference Reference, float LongestToolMm, bool PresetLengths)
{
    /// <summary>Holder length every mill tool sits in (nose to holder face).</summary>
    public const float HolderLengthMm = 60f;

    private bool GaugeLine => Reference == ToolLengthReference.SpindleGaugeLine;
    private float LongestGaugeMm => LongestToolMm + HolderLengthMm;

    /// <summary>Control Z minus the reference point's Z.</summary>
    public float ZControlOffsetMm => GaugeLine ? LongestGaugeMm : 0f;

    /// <summary>Spindle nose above the reference point with this tool in it.</summary>
    public float NoseAboveReferenceMm(float toolLengthMm) => GaugeLine ? LongestGaugeMm : toolLengthMm + HolderLengthMm;

    /// <summary>Tip below the reference point; errorMm = how much longer
    /// than nominal the tool really sticks out.</summary>
    public float TipBelowReferenceMm(float toolLengthMm, float errorMm) =>
        toolLengthMm + HolderLengthMm + errorMm - NoseAboveReferenceMm(toolLengthMm);

    /// <summary>The tool table Z that makes G43 put this tool's real tip on
    /// the programmed Z.</summary>
    public float TableLengthMm(float toolLengthMm, float errorMm) =>
        GaugeLine ? toolLengthMm + HolderLengthMm + errorMm : errorMm;

    /// <summary>Tool table Z when the machine starts.</summary>
    public float InitialTableLengthMm(float toolLengthMm) => PresetLengths ? TableLengthMm(toolLengthMm, 0f) : 0f;
}
