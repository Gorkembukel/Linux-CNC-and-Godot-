using Godot;

namespace CncTrainer.Spec;

public enum MillFrame { CFrame, Gantry }
public enum Workholding { None, Vise, Clamps, Chuck }
public enum StockShape { Block, CylinderAlongX }

[Tool, GlobalClass]
public partial class MillStructure : Resource
{
    /// <summary>CFrame: a column behind the table carries the head.
    /// Gantry: a bridge over the table carries it.</summary>
    [Export] public MillFrame Frame = MillFrame.CFrame;
    [Export] public float BaseWidthMm = 900f;
    [Export] public float BaseDepthMm = 700f;
    [Export] public float ColumnWidthMm = 260f;
    [Export] public float SpindleHousingDiameterMm = 90f;
    /// <summary>Tool pockets of the automatic changer, 0 = tools are changed by hand.</summary>
    [Export] public int MagazinePockets;
    /// <summary>Random: the tool coming out of the spindle goes into the
    /// pocket the new one left, not back to its own.</summary>
    [Export] public bool RandomToolChanger;
    [Export] public float ToolChangeSeconds = 4f;
    [Export] public float MaxToolDiameterMm = 100f;
    /// <summary>Longest tool (out of the holder) the spindle and the changer take.</summary>
    [Export] public float MaxToolLengthMm = 200f;
    [Export] public float TableMaxLoadKg = 150f;

    [ExportGroup("Table")]
    [Export] public float TableSizeXMm = 500f;
    [Export] public float TableSizeYMm = 300f;
    [Export] public float TableThicknessMm = 50f;
    [Export] public int TSlotCount = 3;
    [Export] public Workholding Workholding = Workholding.Vise;

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
