using Godot;

namespace CncTrainer.Spec;

/// <summary>How a lathe holds its tools.</summary>
public enum TurretKind
{
    /// <summary>Indexing disc turret: every tool has a station, the turret turns to the one called.</summary>
    Disc,
    /// <summary>Single tool post: the active tool is swapped in.</summary>
    ToolPost,
}

/// <summary>How a lathe is built: bed, headstock, chuck, tailstock, turret and the stock it starts with.</summary>
[Tool, GlobalClass]
public partial class LatheStructure : Resource
{
    /// <summary>Length of the bed along the spindle axis.</summary>
    [Export] public float BedLengthMm = 600f;
    /// <summary>Depth of the bed and headstock across the machine.</summary>
    [Export] public float BedDepthMm = 300f;
    /// <summary>Direction the tool approaches the spindle centreline from:
    /// 0 = straight above, 30-60 = slant bed, 90 = flat bed with the tool
    /// behind the part, -90 = flat bed with the tool on the operator side.</summary>
    [Export(PropertyHint.Range, "-90,90,1")] public float CrossSlideAngleDeg;
    /// <summary>Length of the headstock along the spindle axis.</summary>
    [Export] public float HeadstockLengthMm = 250f;
    /// <summary>Height of the headstock casing.</summary>
    [Export] public float HeadstockHeightMm = 200f;
    /// <summary>The machine has a tailstock opposite the chuck (it also counts in collisions).</summary>
    [Export] public bool Tailstock;
    /// <summary>Machine Z of the live centre's point; 0 = just beyond the Z travel.</summary>
    [Export] public float TailstockZMm;
    /// <summary>Largest diameter that turns over the bed without hitting it.</summary>
    [Export] public float SwingDiameterMm = 300f;
    /// <summary>Longest free length (out of the chuck) the machine can turn.</summary>
    [Export] public float MaxTurningLengthMm = 500f;
    /// <summary>On: X is programmed as a diameter (G7) unless a program says G8.</summary>
    [Export] public bool DiameterMode;

    /// <summary>Outside diameter of the chuck; the largest bar it grips is a little smaller.</summary>
    [ExportGroup("Chuck")]
    [Export] public float ChuckDiameterMm = 100f;
    /// <summary>Number of jaws on the chuck.</summary>
    [Export] public int ChuckJawCount = 3;
    /// <summary>Highest speed the chuck is rated for; the spindle is limited to it.</summary>
    [Export] public float ChuckMaxRpm = 4000f;
    /// <summary>Highest speed for the four-jaw independent chuck on the rack
    /// (heavier, unbalanced jaws); running faster with it fitted is warned.</summary>
    [Export] public float FourJawMaxRpm = 1500f;
    /// <summary>Highest speed for the collet chuck on the rack.</summary>
    [Export] public float ColletMaxRpm = 5000f;

    /// <summary>The rated speed of whichever chuck is fitted.</summary>
    public float MaxRpmFor(Fixtures.ChuckKind kind) => kind switch
    {
        Fixtures.ChuckKind.FourJawIndependent => FourJawMaxRpm,
        Fixtures.ChuckKind.Collet => ColletMaxRpm,
        _ => ChuckMaxRpm,
    };

    /// <summary>Disc: every tool sits on an indexing turret. ToolPost: one
    /// holder, the active tool is swapped in.</summary>
    [ExportGroup("Turret")]
    [Export] public TurretKind Turret = TurretKind.Disc;
    /// <summary>Number of tool stations on a disc turret; tool N goes in station N.</summary>
    [Export] public int TurretStations = 3;
    /// <summary>Diameter of the turret disc; tools must stick out past it to reach the bar.</summary>
    [Export] public float TurretDiameterMm = 70f;
    /// <summary>Turret centre to insert tip.</summary>
    [Export] public float ToolReachMm = 75f;

    /// <summary>Diameter of the bar in the chuck at start (without hands-on setup).</summary>
    [ExportGroup("Stock")]
    [Export] public float StockDiameterMm = 50f;
    /// <summary>Whole bar, including the part inside the jaws.</summary>
    [Export] public float StockLengthMm = 90f;
    /// <summary>How much of the bar the chuck jaws hold.</summary>
    [Export] public float GripLengthMm = 30f;
    /// <summary>Material beyond Z0 on the free end, removed by facing.</summary>
    [Export] public float FacingAllowanceMm = 5f;

    public float StockZMax => FacingAllowanceMm;
    public float StockZMin => FacingAllowanceMm - StockLengthMm;
}
