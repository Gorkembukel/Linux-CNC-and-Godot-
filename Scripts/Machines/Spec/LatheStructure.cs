using Godot;

namespace CncTrainer.Spec;

public enum TurretKind { Disc, ToolPost }

[Tool, GlobalClass]
public partial class LatheStructure : Resource
{
    [Export] public float BedLengthMm = 600f;
    [Export] public float BedDepthMm = 300f;
    /// <summary>Direction the tool approaches the spindle centreline from:
    /// 0 = straight above, 30-60 = slant bed, 90 = flat bed with the tool
    /// behind the part, -90 = flat bed with the tool on the operator side.</summary>
    [Export(PropertyHint.Range, "-90,90,1")] public float CrossSlideAngleDeg;
    [Export] public float HeadstockLengthMm = 250f;
    [Export] public float HeadstockHeightMm = 200f;
    [Export] public bool Tailstock;

    [ExportGroup("Chuck")]
    [Export] public float ChuckDiameterMm = 100f;
    [Export] public int ChuckJawCount = 3;

    /// <summary>Disc: every tool sits on an indexing turret. ToolPost: one
    /// holder, the active tool is swapped in.</summary>
    [ExportGroup("Turret")]
    [Export] public TurretKind Turret = TurretKind.Disc;
    [Export] public int TurretStations = 3;
    [Export] public float TurretDiameterMm = 70f;
    /// <summary>Turret centre to insert tip.</summary>
    [Export] public float ToolReachMm = 75f;

    [ExportGroup("Stock")]
    [Export] public float StockDiameterMm = 50f;
    /// <summary>Whole bar, including the part inside the jaws.</summary>
    [Export] public float StockLengthMm = 90f;
    [Export] public float GripLengthMm = 30f;
    /// <summary>Material beyond Z0 on the free end, removed by facing.</summary>
    [Export] public float FacingAllowanceMm = 5f;

    public float StockZMax => FacingAllowanceMm;
    public float StockZMin => FacingAllowanceMm - StockLengthMm;
}
