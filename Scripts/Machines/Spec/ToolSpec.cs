using Godot;
using CncTrainer.Machining;
using CncTrainer.Material;

namespace CncTrainer.Spec;

/// <summary>What sort of tool a ToolSpec is.</summary>
public enum ToolKind
{
    /// <summary>Mill: end mill (flat, bull or ball nose by TipRadiusMm), or a form cutter with a profile.</summary>
    EndMill,
    /// <summary>Mill: face mill with inserts around its rim.</summary>
    FaceMill,
    /// <summary>Twist drill (mill spindle, or on a lathe turret along the spindle axis).</summary>
    Drill,
    /// <summary>Mill: chamfer / countersink cutter with a pointed tip (TipAngleDeg).</summary>
    ChamferMill,
    /// <summary>Lathe: outside turning and facing tool.</summary>
    Turning,
    /// <summary>Lathe: parting-off / grooving blade.</summary>
    Parting,
    /// <summary>Lathe: boring bar, cuts inside a hole.</summary>
    Boring,
    /// <summary>Lathe: threading tool.</summary>
    Threading,
    /// <summary>Mill: edge finder for picking up the work zero; does not cut.</summary>
    EdgeFinder,
    /// <summary>Mill: 3D touch probe. DiameterMm is its stylus ball; it
    /// signals the control on contact, so G38.2 finds edges and surfaces.
    /// Does not cut.</summary>
    TouchProbe,
}

/// <summary>One tool of a machine: its number, kind and size, the LinuxCNC tool table values, and optionally its real cutting shape (Geometry).</summary>
[Tool, GlobalClass]
public partial class ToolSpec : Resource
{
    /// <summary>Tool number (the T word); on a turret or a magazine also its station or pocket.</summary>
    [Export] public int Number = 1;
    /// <summary>What sort of tool it is: decides which machines take it and how it cuts.</summary>
    [Export] public ToolKind Kind = ToolKind.EndMill;
    /// <summary>Name in the tool table and in the UI.</summary>
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
    /// <summary>Cutting edges of a mill tool.</summary>
    [Export] public int Flutes = 2;
    /// <summary>LinuxCNC tool orientation (Q), 1-9.</summary>
    [ExportGroup("Lathe tool table")]
    [Export] public int Orientation = 1;
    /// <summary>LinuxCNC tool table front angle (I): direction of the leading cutting edge; with an insert it comes from the insert.</summary>
    [Export] public float FrontAngleDeg = 95f;
    /// <summary>LinuxCNC tool table back angle (J): direction of the trailing edge; with an insert it comes from the insert.</summary>
    [Export] public float BackAngleDeg = 155f;
    /// <summary>Colour of the cutting part.</summary>
    [ExportGroup("")]
    [Export] public Color Color = new(0.85f, 0.75f, 0.35f);
    /// <summary>The real cutting shape (insert, form); null = the simple model above.</summary>
    [Export] public ToolGeometry Geometry;

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
    /// <summary>A measuring tool (edge finder, touch probe): never cuts,
    /// breaks when pushed into the work.</summary>
    public bool Measures => Kind is ToolKind.EdgeFinder or ToolKind.TouchProbe;

    public CutterShape Cutter() => !IsLatheTool && Geometry is { MillProfileMm.Length: >= 2 } g
        ? CutterShape.Form(g.MillProfileMm, LengthMm)
        : new(DiameterMm / 2f,
        HasPoint ? 0f : Mathf.Min(TipRadiusMm, DiameterMm / 2f), LengthMm, HasPoint ? TipAngleDeg : 0f);

    public ToolLimits Limits() => new(IsLatheTool, Kind == ToolKind.Parting, DiameterMm, LengthMm, Flutes, Kind);

    /// <summary>The bridge reports the table's D column as the active
    /// tool's size: cutter diameter on a mill, nose radius on a lathe.</summary>
    public ToolDef ToDef(bool lathe)
    {
        if (!lathe || Geometry == null || Geometry.Params().IncludedDeg <= 0f)
            return new(Number, lathe ? NoseRadiusMm : DiameterMm, lathe, FrontAngleDeg, BackAngleDeg, Orientation, Description);
        float front = Geometry.ApproachDeg, back = front + Geometry.Params().IncludedDeg;
        return new(Number, NoseRadiusMm, true, front, back, Orientation, Description);
    }

    /// <summary>Lathe nose radius: the insert's when there is one.</summary>
    public float NoseRadiusMm => Geometry != null && IsLatheTool ? Geometry.NoseRadiusMm : TipRadiusMm;

    /// <summary>The cutting outline when this is a lathe tool with a real insert.</summary>
    public CncTrainer.Material.ToolProfile LatheProfile =>
        Geometry != null && Kind is ToolKind.Turning or ToolKind.Parting or ToolKind.Threading or ToolKind.Boring
            ? Geometry.Profile(Kind == ToolKind.Boring) : null;
}
