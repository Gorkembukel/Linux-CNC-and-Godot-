using System;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;
using static CncTrainer.Spec.AxisCarrier;
using static System.FormattableString;

namespace CncTrainer.Spec;

/// <summary>Starting points for the machine generator: typical machine
/// classes with realistic travels, to be adjusted to a specific machine.</summary>
public static class MachinePresets
{
    public static readonly (string Label, Func<MachineSpec> Create)[] All =
    {
        ("Benchtop 3-axis mill (runs demo_mill_pocket)", BenchtopMill),
        ("Benchtop lathe (runs demo_facing_turning)", BenchtopLathe),
        ("Benchtop lathe, hands-on chuck setup", HandsOnLathe),
        ("Vertical machining centre, 3-axis C-frame", VerticalMachiningCentre),
        ("Gantry router, 3-axis", GantryRouter),
        ("4-axis VMC with A rotary indexer", FourAxisIndexer),
        ("5-axis trunnion (A tilt + C rotary table)", FiveAxisTrunnion),
        ("5-axis swivel head (B head + C rotary table)", FiveAxisSwivelHead),
        ("Flat-bed lathe with tool post and tailstock", FlatBedLathe),
        ("Slant-bed turning centre with disc turret", SlantBedLathe),
    };

    private static Godot.Collections.Array<T> List<[MustBeVariant] T>(params T[] items) => new(items);

    private static ToolSpec EndMill(int number, float diameter, float cornerRadius = 0f, float length = 50f) =>
        new(number, ToolKind.EndMill, cornerRadius >= diameter / 2f ? Invariant($"{diameter:0.#}mm ball nose")
            : cornerRadius > 0f ? Invariant($"{diameter:0.#}mm bull nose R{cornerRadius:0.#}") : Invariant($"{diameter:0.#}mm flat end mill"),
            diameter, cornerRadius, length);

    private static ToolSpec EdgeFinder(int number) =>
        new(number, ToolKind.EdgeFinder, "10mm edge finder", 10f, lengthMm: 40f, color: new Color(0.8f, 0.8f, 0.85f));

    private static ToolSpec Drill(int number, float diameter, float length = 80f) =>
        new(number, ToolKind.Drill, Invariant($"{diameter:0.#}mm drill"), diameter, lengthMm: length, tipAngleDeg: 118f, color: new Color(0.6f, 0.6f, 0.65f));

    private static ToolSpec Chamfer(int number, float diameter, float angle = 90f) =>
        new(number, ToolKind.ChamferMill, Invariant($"{angle:0}deg chamfer mill"), diameter, lengthMm: 40f, tipAngleDeg: angle, color: new Color(0.7f, 0.5f, 0.3f));

    private static ToolSpec FaceMill(int number, float diameter) =>
        new(number, ToolKind.FaceMill, Invariant($"{diameter:0.#}mm face mill"), diameter, lengthMm: 50f, color: new Color(0.4f, 0.42f, 0.45f));

    private static Godot.Collections.Array<ToolSpec> TurningTools() => List(
        new ToolSpec(1, ToolKind.Turning, "roughing turn tool, 0.8mm nose radius", 0f, 0.8f, color: new Color(0.85f, 0.65f, 0.2f)),
        new ToolSpec(2, ToolKind.Turning, "finishing turn tool, 0.4mm nose radius", 0f, 0.4f, color: new Color(0.3f, 0.8f, 0.9f)),
        new ToolSpec(3, ToolKind.Parting, "parting / grooving tool, 3mm wide", 0f, 0.2f, widthMm: 3f, orientation: 3,
            frontAngleDeg: 0f, backAngleDeg: 90f, color: new Color(0.9f, 0.9f, 0.95f)));

    public static MachineSpec BenchtopMill() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "BenchMill",
        ManualSetup = true,
        DemoProgram = "nc_files/demo_mill_pocket.ngc",
        Axes = List(
            new AxisSpec(AxisId.Y, Tool, -100f, 100f),
            new AxisSpec(AxisId.X, Tool, -150f, 150f),
            new AxisSpec(AxisId.Z, Tool, -40f, 120f, home: 50f, homeSequence: 0)),
        Tools = List(EndMill(1, 10f), EndMill(2, 6f), EdgeFinder(9)),
        Mill = new MillStructure
        {
            Frame = MillFrame.Gantry, BaseWidthMm = 600f, BaseDepthMm = 500f,
            TableSizeXMm = 500f, TableSizeYMm = 360f, TableThicknessMm = 40f,
        },
    };

    public static MachineSpec BenchtopLathe() => new()
    {
        Kind = MachineKind.Lathe,
        MachineName = "BenchLathe",
        DemoProgram = "nc_files/demo_facing_turning.ngc",
        UseToolChangePosition = true,
        ToolChangePosition = new Vector3(80f, 0f, 40f),
        StockMaterial = MaterialKind.Steel,
        BodyColor = new Color(0.15f, 0.35f, 0.22f),
        Axes = List(
            new AxisSpec(AxisId.X, Tool, -5f, 150f, homeSequence: 0),
            new AxisSpec(AxisId.Z, Tool, -300f, 50f)),
        Tools = TurningTools(),
        Lathe = new LatheStructure { CrossSlideAngleDeg = 60f },
    };

    /// <summary>The benchtop lathe with an empty chuck. The jaws sit at
    /// machine Z-150 so a bar can stick out well past 100 mm and still be
    /// clear of the turret where the machine wakes up (X0 Z0), and X homes
    /// away from the centreline.</summary>
    public static MachineSpec HandsOnLathe()
    {
        var spec = BenchtopLathe();
        spec.MachineName = "BenchLathe";
        spec.ManualSetup = true;
        spec.FindAxis(AxisId.X).Home = 100f;
        spec.Lathe.StockLengthMm = 185f;
        return spec;
    }

    public static MachineSpec VerticalMachiningCentre() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "Vmc",
        ManualSetup = true,
        DemoProgram = "nc_files/vmc_cok_takimli.ngc",
        WorkHeightMm = 950f,
        Enclosure = true,
        BodyColor = new Color(0.82f, 0.82f, 0.8f),
        Axes = List(
            new AxisSpec(AxisId.Y, Workpiece, -203f, 203f, maxVelocity: 400f, maxAcceleration: 2000f),
            new AxisSpec(AxisId.X, Workpiece, -381f, 381f, maxVelocity: 400f, maxAcceleration: 2000f),
            new AxisSpec(AxisId.Z, Tool, -60f, 448f, home: 400f, homeSequence: 0, maxVelocity: 400f, maxAcceleration: 2000f)),
        Tools = List(EndMill(1, 12f, length: 60f), EndMill(2, 6f), EndMill(3, 10f, 5f), Drill(4, 8.5f), FaceMill(5, 50f), Chamfer(6, 10f), EdgeFinder(9)),
        Mill = new MillStructure
        {
            Frame = MillFrame.CFrame, BaseWidthMm = 1500f, BaseDepthMm = 1300f, ColumnWidthMm = 450f,
            SpindleHousingDiameterMm = 140f, MagazinePockets = 20,
            TableSizeXMm = 914f, TableSizeYMm = 356f, TableThicknessMm = 70f, TSlotCount = 3,
            StockSizeXMm = 150f, StockSizeYMm = 100f, StockHeightMm = 50f,
        },
    };

    public static MachineSpec GantryRouter() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "Router",
        WorkHeightMm = 850f,
        Axes = List(
            new AxisSpec(AxisId.Y, Tool, -300f, 300f, maxVelocity: 250f),
            new AxisSpec(AxisId.X, Tool, -450f, 450f, maxVelocity: 250f),
            new AxisSpec(AxisId.Z, Tool, -25f, 120f, home: 100f, homeSequence: 0)),
        Tools = List(EndMill(1, 6f, length: 40f), EndMill(2, 3f, length: 30f), Chamfer(3, 12f)),
        Mill = new MillStructure
        {
            Frame = MillFrame.Gantry, BaseWidthMm = 1200f, BaseDepthMm = 900f, SpindleHousingDiameterMm = 80f,
            TableSizeXMm = 1000f, TableSizeYMm = 700f, TableThicknessMm = 30f, TSlotCount = 6,
            Workholding = Workholding.Clamps,
            StockSizeXMm = 400f, StockSizeYMm = 300f, StockHeightMm = 20f, VoxelStepMm = 0.75f,
        },
    };

    public static MachineSpec FourAxisIndexer() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "Vmc4Axis",
        DemoProgram = "nc_files/indexer4_altigen_helis.ngc",
        WorkHeightMm = 1000f,
        Enclosure = true,
        BodyColor = new Color(0.82f, 0.82f, 0.8f),
        Axes = List(
            new AxisSpec(AxisId.Y, Workpiece, -200f, 200f, maxVelocity: 300f),
            new AxisSpec(AxisId.X, Workpiece, -350f, 350f, maxVelocity: 300f),
            new AxisSpec(AxisId.A, Workpiece, -360f, 360f, maxVelocity: 120f, pivotMm: new Vector3(0f, 0f, -30f)),
            new AxisSpec(AxisId.Z, Tool, -60f, 400f, home: 350f, homeSequence: 0, maxVelocity: 300f)),
        Tools = List(EndMill(1, 10f), EndMill(2, 6f, 3f), Drill(3, 6f), Chamfer(4, 10f)),
        Mill = new MillStructure
        {
            Frame = MillFrame.CFrame, BaseWidthMm = 1400f, BaseDepthMm = 1200f, ColumnWidthMm = 400f,
            SpindleHousingDiameterMm = 130f, MagazinePockets = 16,
            TableSizeXMm = 800f, TableSizeYMm = 350f, TableThicknessMm = 60f,
            Workholding = Workholding.Chuck, StockShape = StockShape.CylinderAlongX,
            StockSizeXMm = 180f, StockSizeYMm = 60f,
        },
    };

    public static MachineSpec FiveAxisTrunnion() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "Trunnion5Axis",
        DemoProgram = "nc_files/trunnion5_bes_yuz.ngc",
        WorkHeightMm = 1050f,
        Enclosure = true,
        BodyColor = new Color(0.82f, 0.82f, 0.8f),
        Axes = List(
            new AxisSpec(AxisId.X, Tool, -380f, 380f, maxVelocity: 400f),
            new AxisSpec(AxisId.Y, Tool, -250f, 250f, maxVelocity: 400f),
            new AxisSpec(AxisId.Z, Tool, -80f, 420f, home: 380f, homeSequence: 0, maxVelocity: 400f),
            new AxisSpec(AxisId.A, Workpiece, -35f, 110f, maxVelocity: 90f, pivotMm: new Vector3(0f, 0f, -20f)),
            new AxisSpec(AxisId.C, Workpiece, -360f, 360f, maxVelocity: 150f)),
        Tools = List(EndMill(1, 10f, length: 70f), EndMill(2, 8f, 4f, 70f), EndMill(3, 6f, 0.5f, 60f), Drill(4, 6.8f), Chamfer(5, 10f)),
        Mill = new MillStructure
        {
            Frame = MillFrame.CFrame, BaseWidthMm = 1500f, BaseDepthMm = 1400f, ColumnWidthMm = 450f,
            SpindleHousingDiameterMm = 130f, MagazinePockets = 24,
            TableSizeXMm = 350f, TableSizeYMm = 350f, Workholding = Workholding.Clamps,
            StockSizeXMm = 100f, StockSizeYMm = 100f, StockHeightMm = 60f,
        },
    };

    public static MachineSpec FiveAxisSwivelHead() => new()
    {
        Kind = MachineKind.Mill,
        MachineName = "SwivelHead5Axis",
        WorkHeightMm = 1000f,
        Enclosure = true,
        BodyColor = new Color(0.82f, 0.82f, 0.8f),
        Axes = List(
            new AxisSpec(AxisId.Y, Workpiece, -250f, 250f, maxVelocity: 400f),
            new AxisSpec(AxisId.X, Workpiece, -350f, 350f, maxVelocity: 400f),
            new AxisSpec(AxisId.C, Workpiece, -360f, 360f, maxVelocity: 150f),
            new AxisSpec(AxisId.Z, Tool, -80f, 450f, home: 400f, homeSequence: 0, maxVelocity: 400f),
            new AxisSpec(AxisId.B, Tool, -110f, 110f, maxVelocity: 90f, pivotMm: new Vector3(0f, 0f, 220f))),
        Tools = List(EndMill(1, 10f, length: 70f), EndMill(2, 8f, 4f, 70f), Drill(3, 6.8f), Chamfer(4, 10f)),
        Mill = new MillStructure
        {
            Frame = MillFrame.CFrame, BaseWidthMm = 1500f, BaseDepthMm = 1400f, ColumnWidthMm = 450f,
            SpindleHousingDiameterMm = 120f, MagazinePockets = 24,
            TableSizeXMm = 500f, TableSizeYMm = 500f, Workholding = Workholding.Clamps,
            StockSizeXMm = 120f, StockSizeYMm = 120f, StockHeightMm = 80f,
        },
    };

    public static MachineSpec FlatBedLathe() => new()
    {
        Kind = MachineKind.Lathe,
        MachineName = "FlatBedLathe",
        WorkHeightMm = 1050f,
        UseToolChangePosition = true,
        ToolChangePosition = new Vector3(120f, 0f, 60f),
        StockMaterial = MaterialKind.Steel,
        BodyColor = new Color(0.25f, 0.33f, 0.4f),
        Axes = List(
            new AxisSpec(AxisId.X, Tool, -5f, 180f, homeSequence: 0),
            new AxisSpec(AxisId.Z, Tool, -700f, 80f)),
        Tools = TurningTools(),
        Lathe = new LatheStructure
        {
            BedLengthMm = 1500f, BedDepthMm = 450f, CrossSlideAngleDeg = -90f, HeadstockLengthMm = 450f, HeadstockHeightMm = 400f,
            Tailstock = true, ChuckDiameterMm = 200f, Turret = TurretKind.ToolPost, ToolReachMm = 70f,
            StockDiameterMm = 60f, StockLengthMm = 250f, GripLengthMm = 50f,
        },
    };

    public static MachineSpec SlantBedLathe() => new()
    {
        Kind = MachineKind.Lathe,
        MachineName = "TurningCentre",
        WorkHeightMm = 1100f,
        Enclosure = true,
        UseToolChangePosition = true,
        ToolChangePosition = new Vector3(150f, 0f, 60f),
        StockMaterial = MaterialKind.Steel,
        BodyColor = new Color(0.82f, 0.82f, 0.8f),
        Axes = List(
            new AxisSpec(AxisId.X, Tool, -5f, 200f, homeSequence: 0, maxVelocity: 300f),
            new AxisSpec(AxisId.Z, Tool, -400f, 80f, maxVelocity: 300f)),
        Tools = List(
            new ToolSpec(1, ToolKind.Turning, "roughing turn tool, 0.8mm nose radius", 0f, 0.8f, color: new Color(0.85f, 0.65f, 0.2f)),
            new ToolSpec(2, ToolKind.Turning, "finishing turn tool, 0.4mm nose radius", 0f, 0.4f, color: new Color(0.3f, 0.8f, 0.9f)),
            new ToolSpec(3, ToolKind.Parting, "parting tool, 3mm wide", 0f, 0.2f, widthMm: 3f, orientation: 3, frontAngleDeg: 0f, backAngleDeg: 90f),
            new ToolSpec(4, ToolKind.Threading, "60deg threading tool", 0f, 0.1f, orientation: 6, color: new Color(0.8f, 0.4f, 0.4f)),
            new ToolSpec(5, ToolKind.Boring, "16mm boring bar", 16f, 0.4f, lengthMm: 80f, orientation: 2),
            new ToolSpec(6, ToolKind.Drill, "12mm drill", 12f, 0f, lengthMm: 90f, orientation: 7, color: new Color(0.6f, 0.6f, 0.65f))),
        Lathe = new LatheStructure
        {
            BedLengthMm = 1300f, BedDepthMm = 600f, CrossSlideAngleDeg = 45f, HeadstockLengthMm = 500f, HeadstockHeightMm = 450f,
            ChuckDiameterMm = 210f, Turret = TurretKind.Disc, TurretStations = 8, TurretDiameterMm = 260f, ToolReachMm = 190f,
            StockDiameterMm = 65f, StockLengthMm = 160f, GripLengthMm = 40f,
        },
    };
}
