using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Spec;

public enum AxisCarrier { Tool, Workpiece }

[Tool, GlobalClass]
public partial class AxisSpec : Resource
{
    /// <summary>Which axis this is: X, Y, Z are linear (mm), A, B, C rotary (degrees).</summary>
    [Export] public AxisId Letter = AxisId.X;
    /// <summary>Which side of the machine this axis moves: the spindle
    /// (gantry, ram, swivel head) or the workpiece (table, saddle, trunnion).</summary>
    [Export] public AxisCarrier Carrier = AxisCarrier.Tool;
    /// <summary>mm for X/Y/Z, degrees for A/B/C.</summary>
    [Export] public float MinLimit = -100f;
    [Export] public float MaxLimit = 100f;
    [Export] public float MaxVelocity = 100f;
    [Export] public float MaxAcceleration = 800f;
    [Export] public float Home;
    /// <summary>Axes with the lowest number home first.</summary>
    [Export] public int HomeSequence = 1;
    /// <summary>Rotary axes only: a point on the rotation centreline, in
    /// machine X/Y/Z mm from work zero.</summary>
    [Export] public Vector3 PivotMm;

    /// <summary>The sign is the direction the axis runs to find its home switch.</summary>
    [ExportGroup("Homing")]
    [Export] public float HomeSearchVelocity = 20f;
    [Export] public float HomeLatchVelocity = 5f;

    /// <summary>Real play in the drive: after a reversal the axis moves
    /// this far before the tool does.</summary>
    [ExportGroup("Accuracy")]
    [Export] public float MechanicalBacklashMm;
    /// <summary>What the control adds back on a reversal (INI BACKLASH).</summary>
    [Export] public float BacklashCompensationMm;

    /// <summary>Rotary only: turns endlessly, 0-360, shortest way round.</summary>
    [ExportGroup("Rotary")]
    [Export] public bool Wrapped;

    public AxisSpec() { }

    public AxisSpec(AxisId letter, AxisCarrier carrier, float min, float max, float home = 0f, int homeSequence = 1,
        float maxVelocity = 100f, float maxAcceleration = 800f, Vector3 pivotMm = default)
    {
        Letter = letter; Carrier = carrier; MinLimit = min; MaxLimit = max; Home = home; HomeSequence = homeSequence;
        MaxVelocity = maxVelocity; MaxAcceleration = maxAcceleration; PivotMm = pivotMm;
    }

    public AxisDef ToDef() => new(Letter, Carrier == AxisCarrier.Workpiece, MinLimit, MaxLimit,
        MaxVelocity, MaxAcceleration, Home, HomeSequence, PivotMm,
        HomeSearchVelocity, HomeLatchVelocity, BacklashCompensationMm, Wrapped && Letter >= AxisId.A);

    /// <summary>Play the control does not take out.</summary>
    public float UncompensatedPlayMm => Mathf.Max(0f, MechanicalBacklashMm - BacklashCompensationMm);
}
