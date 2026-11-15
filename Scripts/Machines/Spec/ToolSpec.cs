using Godot;
using CncTrainer.Material;

namespace CncTrainer.Spec;

public enum ToolKind { EndMill, FaceMill, Drill, ChamferMill, Turning, Parting, Boring, Threading, EdgeFinder }

[Tool, GlobalClass]
public partial class ToolSpec : Resource
{
    [Export] public int Number = 1;
    [Export] public ToolKind Kind = ToolKind.EndMill;
    [Export] public string Description = "";
    /// <summary>Cutter diameter (mill tools, drills, boring bars).</summary>
    [Export] public float DiameterMm = 10f;
    /// <summary>Lathe tools: insert nose radius. End mills: corner radius
    /// (0 = flat, half the diameter = ball nose).</summary>
    [Export] public float TipRadiusMm;
    /// <summary>How far the tool sticks out of its holder.</summary>
    [Export] public float LengthMm = 50f;
    /// <summary>Included point angle of a drill or chamfer mill.</summary>
    [Export] public float TipAngleDeg = 118f;
    /// <summary>Parting / grooving insert width.</summary>
    [Export] public float WidthMm = 3f;
    /// <summary>LinuxCNC tool orientation (Q), 1-9.</summary>
    [ExportGroup("Lathe tool table")]
    [Export] public int Orientation = 1;
    [Export] public float FrontAngleDeg = 95f;
    [Export] public float BackAngleDeg = 155f;
    [ExportGroup("")]
    [Export] public Color Color = new(0.85f, 0.75f, 0.35f);

    public ToolSpec() { }

    public ToolSpec(int number, ToolKind kind, string description, float diameterMm, float tipRadiusMm = 0f,
        float lengthMm = 50f, float tipAngleDeg = 118f, float widthMm = 3f, int orientation = 1,
        float frontAngleDeg = 95f, float backAngleDeg = 155f, Color? color = null)
    {
        Number = number; Kind = kind; Description = description; DiameterMm = diameterMm; TipRadiusMm = tipRadiusMm;
        LengthMm = lengthMm; TipAngleDeg = tipAngleDeg; WidthMm = widthMm; Orientation = orientation;
        FrontAngleDeg = frontAngleDeg; BackAngleDeg = backAngleDeg;
        Color = color ?? Color;
    }

    public bool IsLatheTool => Kind is ToolKind.Turning or ToolKind.Parting or ToolKind.Boring or ToolKind.Threading;
    public bool HasPoint => Kind is ToolKind.Drill or ToolKind.ChamferMill;

    public CutterShape Cutter() => new(DiameterMm / 2f,
        HasPoint ? 0f : Mathf.Min(TipRadiusMm, DiameterMm / 2f), LengthMm, HasPoint ? TipAngleDeg : 0f);

    /// <summary>The bridge reports the table's D column as the active
    /// tool's size: cutter diameter on a mill, nose radius on a lathe.</summary>
    public ToolDef ToDef(bool lathe) => new(Number, lathe ? TipRadiusMm : DiameterMm, lathe,
        FrontAngleDeg, BackAngleDeg, Orientation, Description);
}
