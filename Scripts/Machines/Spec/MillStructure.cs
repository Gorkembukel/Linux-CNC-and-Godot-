using Godot;

namespace CncTrainer.Spec;

/// <summary>How a mill carries its spindle head.</summary>
public enum MillFrame
{
    /// <summary>A column behind the table carries the head (VMC, bench mill).</summary>
    CFrame,
    /// <summary>A bridge over the table carries the head (router, gantry mill).</summary>
    Gantry,
}

/// <summary>What holds the stock on a mill.</summary>
public enum Workholding
{
    /// <summary>Nothing: the stock sits fixed at machine zero.</summary>
    None,
    /// <summary>A machine vise bolted to the table.</summary>
    Vise,
    /// <summary>Step clamps straight onto the table.</summary>
    Clamps,
    /// <summary>A chuck on the rotary axis (indexer).</summary>
    Chuck,
}

/// <summary>Shape of raw stock.</summary>
public enum StockShape
{
    /// <summary>Rectangular block.</summary>
    Block,
    /// <summary>Round bar lying along its X axis.</summary>
    CylinderAlongX,
}

/// <summary>How a mill is built: frame, table, tool changer, workholding and the stock it starts with.</summary>
[Tool, GlobalClass]
public partial class MillStructure : Resource
{
    /// <summary>CFrame: a column behind the table carries the head.
    /// Gantry: a bridge over the table carries it.</summary>
    [Export] public MillFrame Frame = MillFrame.CFrame;
    /// <summary>Width of the machine base.</summary>
    [Export] public float BaseWidthMm = 900f;
    /// <summary>Depth of the machine base.</summary>
    [Export] public float BaseDepthMm = 700f;
    /// <summary>Width of the column (C-frame) or of the gantry uprights.</summary>
    [Export] public float ColumnWidthMm = 260f;
    /// <summary>Diameter of the spindle housing; its nose counts in collisions.</summary>
    [Export] public float SpindleHousingDiameterMm = 90f;
    /// <summary>Tool pockets of the automatic changer, 0 = tools are changed by hand.</summary>
    [Export] public int MagazinePockets;
    /// <summary>Random: the tool coming out of the spindle goes into the
    /// pocket the new one left, not back to its own.</summary>
    [Export] public bool RandomToolChanger;
    /// <summary>How long the automatic changer takes for one tool change.</summary>
    [Export] public float ToolChangeSeconds = 4f;
    /// <summary>Largest tool diameter the spindle and the changer take.</summary>
    [Export] public float MaxToolDiameterMm = 100f;
    /// <summary>Longest tool (out of the holder) the spindle and the changer take.</summary>
    [Export] public float MaxToolLengthMm = 200f;
    /// <summary>Most weight (fixture and stock) the table carries.</summary>
    [Export] public float TableMaxLoadKg = 150f;

    /// <summary>What the control's Z means without a tool length offset.
    /// SpindleGaugeLine (real machine): Z is the spindle gauge line, the
    /// tool table holds whole tool lengths, and a program that forgets G43
    /// drives the tip a full tool length below the programmed Z.
    /// NominalToolTip (simplified): Z is the tip of a catalogue-length tool,
    /// the table holds only each tool's setting error, and forgetting G43
    /// costs just that error.</summary>
    [ExportGroup("Tool Length")]
    [Export] public ToolLengthReference ToolLengthReference = ToolLengthReference.SpindleGaugeLine;
    /// <summary>On: the tool table starts with every tool's catalogue length,
    /// as if set on a presetter, so an unmeasured tool is off only by its
    /// setting error. Off: the table starts at zero and every tool must be
    /// measured before G43 is safe. Only matters with SpindleGaugeLine.</summary>
    [Export] public bool PresetToolLengths = true;

    /// <summary>How far the touch probe's stylus gives before it breaks
    /// (mm). A G38 move stops well inside it; a G0/G1 into the work does not.</summary>
    [ExportGroup("Touch Probe")]
    [Export(PropertyHint.Range, "0.5,10,0.1")] public float ProbeOvertravelMm = 3f;
    /// <summary>How early the probe signals the control (seconds of travel
    /// ahead), making up for the trip from the simulation to LinuxCNC so
    /// G38 stops on the surface rather than a little past it. 0 = signal
    /// only on real contact.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.005")] public float ProbeSignalLeadSec = 0f;

    /// <summary>Table length along X.</summary>
    [ExportGroup("Table")]
    [Export] public float TableSizeXMm = 500f;
    /// <summary>Table width along Y.</summary>
    [Export] public float TableSizeYMm = 300f;
    /// <summary>Table thickness.</summary>
    [Export] public float TableThicknessMm = 50f;
    /// <summary>Number of T-slots along the table; fixtures bolt into them.</summary>
    [Export] public int TSlotCount = 3;
    /// <summary>What holds the stock: a vise, step clamps, or a chuck on the rotary axis.</summary>
    [Export] public Workholding Workholding = Workholding.Vise;

    /// <summary>Shape of the stock at start: a block or a round bar.</summary>
    [ExportGroup("Stock")]
    [Export] public StockShape StockShape = StockShape.Block;
    /// <summary>Block: size along X. Cylinder: length.</summary>
    [Export] public float StockSizeXMm = 120f;
    /// <summary>Block: size along Y. Cylinder: diameter.</summary>
    [Export] public float StockSizeYMm = 80f;
    /// <summary>Block only; work zero is the centre of the top face.</summary>
    [Export] public float StockHeightMm = 30f;
    /// <summary>Resolution of the cut surface. Grows automatically for
    /// stock too large to hold in memory at this step.</summary>
    [Export] public float VoxelStepMm = 0.5f;

    public float StockHeight => StockShape == StockShape.Block ? StockHeightMm : StockSizeYMm;
}
