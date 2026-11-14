using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Spec;

public enum AxisCarrier { Tool, Workpiece }

[Tool, GlobalClass]
public partial class AxisSpec : Resource
{
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

    public AxisSpec() { }

    public AxisSpec(AxisId letter, AxisCarrier carrier, float min, float max, float home = 0f, int homeSequence = 1,
        float maxVelocity = 100f, float maxAcceleration = 800f, Vector3 pivotMm = default)
    {
        Letter = letter; Carrier = carrier; MinLimit = min; MaxLimit = max; Home = home; HomeSequence = homeSequence;
        MaxVelocity = maxVelocity; MaxAcceleration = maxAcceleration; PivotMm = pivotMm;
    }

    public AxisDef ToDef() => new(Letter, Carrier == AxisCarrier.Workpiece, MinLimit, MaxLimit,
        MaxVelocity, MaxAcceleration, Home, HomeSequence, PivotMm);
}
