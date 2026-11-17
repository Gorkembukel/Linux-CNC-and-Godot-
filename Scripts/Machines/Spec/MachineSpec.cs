using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Chips;

namespace CncTrainer.Spec;

public enum MachineKind { Mill, Lathe }

/// <summary>
/// Everything that defines one machine: its LinuxCNC configuration, axes,
/// tools and physical build. ProceduralMachine builds the scene and the INI
/// from this and nothing else.
/// </summary>
[Tool, GlobalClass]
public partial class MachineSpec : Resource
{
    [Export] public MachineKind Kind = MachineKind.Mill;
    [Export] public string MachineName = "Machine";

    /// <summary>LinuxCNC allows one running instance per WSL distro.</summary>
    [ExportGroup("LinuxCNC")]
    [Export] public string WslDistro = "Ubuntu";
    [Export] public int StatePort = 9204;
    [Export] public int CommandPort = 9205;
    [Export] public double RateHz = 100.0;
    /// <summary>Path inside linuxcnc-config/, e.g. nc_files/part.ngc.</summary>
    [Export] public string DemoProgram = "";
    [Export] public bool UseToolChangePosition;
    /// <summary>Machine X/Y/Z the control moves to before a tool change.</summary>
    [Export] public Vector3 ToolChangePosition;

    /// <summary>Mills: list order is the kinematic chain, the first entry
    /// of each side sits closest to the machine base.</summary>
    [ExportGroup("Axes and tools")]
    [Export] public Godot.Collections.Array<AxisSpec> Axes = new();
    [Export] public Godot.Collections.Array<ToolSpec> Tools = new();

    /// <summary>Floor to work zero (mill) or spindle centreline (lathe).</summary>
    [ExportGroup("Body")]
    [Export] public float WorkHeightMm = 1100f;
    [Export] public bool Enclosure;
    [Export] public Color BodyColor = new(0.16f, 0.27f, 0.42f);
    [Export] public Color AccentColor = new(0.22f, 0.23f, 0.25f);
    [Export] public MaterialKind StockMaterial = MaterialKind.Aluminum;
    /// <summary>Continuous spindle power. A cut needing 1.5 times this stalls the spindle.</summary>
    [Export] public float SpindlePowerKw = 2.2f;
    /// <summary>The machine starts empty: the player brings the fixture
    /// and the stock, bolts and clamps them, and sets the work zero. Off =
    /// the nominal stock is already in place at machine zero.</summary>
    [Export] public bool ManualSetup;

    [ExportGroup("Structure")]
    [Export] public MillStructure Mill;
    [Export] public LatheStructure Lathe;

    public string ConfigStem => "gen_" + MachineName;

    public AxisDef[] AxisDefs() => Axes.Where(a => a != null).Select(a => a.ToDef()).ToArray();

    public ToolSpec FindTool(int number) => Tools.FirstOrDefault(t => t != null && t.Number == number);

    public AxisSpec FindAxis(AxisId letter) => Axes.FirstOrDefault(a => a != null && a.Letter == letter);

    public string Ini() => LinuxCncConfigWriter.Ini(MachineName, ConfigStem, AxisDefs(),
        UseToolChangePosition ? ToolChangePosition : null);

    public string ToolTable() => LinuxCncConfigWriter.ToolTable(
        Tools.Where(t => t != null).Select(t => t.ToDef(Kind == MachineKind.Lathe)));

    /// <summary>Writes this machine's INI and tool table next to the
    /// hand-written ones; the bridge deploys the whole folder into WSL.</summary>
    public void WriteConfigFiles()
    {
        string dir = ProjectSettings.GlobalizePath("res://linuxcnc-config/");
        System.IO.File.WriteAllText(dir + ConfigStem + ".ini", Ini());
        System.IO.File.WriteAllText(dir + ConfigStem + ".tbl", ToolTable());
    }

    /// <summary>Problems that would stop the machine from being built or
    /// LinuxCNC from starting. Empty = usable.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        // The name ends up in file names and on a shell command line.
        if (!Regex.IsMatch(MachineName ?? "", "^[A-Za-z][A-Za-z0-9_]*$"))
            problems.Add("Machine name must start with a letter and use only letters, digits and _.");
        if (string.IsNullOrWhiteSpace(WslDistro))
            problems.Add("WSL distro is empty.");
        if (StatePort == CommandPort)
            problems.Add("State port and command port must differ.");

        if (SpindlePowerKw <= 0f)
            problems.Add("Spindle power must be positive.");
        if (Kind == MachineKind.Mill && Mill == null)
            problems.Add("Mill structure is missing.");
        if (Kind == MachineKind.Lathe && Lathe == null)
            problems.Add("Lathe structure is missing.");

        if (Axes.Any(a => a == null))
            problems.Add("The axis list has an empty slot.");
        var axes = Axes.Where(a => a != null).ToArray();
        foreach (var group in axes.GroupBy(a => a.Letter).Where(g => g.Count() > 1))
            problems.Add($"Axis {group.Key} is listed {group.Count()} times.");
        var required = Kind == MachineKind.Lathe ? new[] { AxisId.X, AxisId.Z } : new[] { AxisId.X, AxisId.Y, AxisId.Z };
        foreach (var letter in required.Where(l => axes.All(a => a.Letter != l)))
            problems.Add($"A {Kind.ToString().ToLowerInvariant()} needs a {letter} axis.");
        if (Kind == MachineKind.Lathe && axes.Any(a => a.Letter != AxisId.X && a.Letter != AxisId.Z))
            problems.Add("Lathes support the X and Z axes only.");

        foreach (var a in axes)
        {
            if (a.MinLimit >= a.MaxLimit)
                problems.Add($"Axis {a.Letter}: min limit must be below max limit.");
            else if (a.Home < a.MinLimit || a.Home > a.MaxLimit)
                problems.Add($"Axis {a.Letter}: home position {a.Home} is outside its travel.");
            if (a.MaxVelocity <= 0f || a.MaxAcceleration <= 0f)
                problems.Add($"Axis {a.Letter}: velocity and acceleration must be positive.");
        }
        if (UseToolChangePosition)
            foreach (var (letter, value) in new[] { (AxisId.X, ToolChangePosition.X), (AxisId.Y, ToolChangePosition.Y), (AxisId.Z, ToolChangePosition.Z) })
                if (FindAxis(letter) is AxisSpec a && (value < a.MinLimit || value > a.MaxLimit))
                    problems.Add($"Tool change position {letter}={value} is outside the axis travel.");

        if (Tools.Any(t => t == null))
            problems.Add("The tool list has an empty slot.");
        var tools = Tools.Where(t => t != null).ToArray();
        if (tools.Length == 0)
            problems.Add("Add at least one tool.");
        foreach (var group in tools.GroupBy(t => t.Number).Where(g => g.Count() > 1))
            problems.Add($"Tool number T{group.Key} is used {group.Count()} times.");
        foreach (var t in tools)
        {
            if (t.Number < 1)
                problems.Add("Tool numbers start at 1.");
            bool millOnly = t.Kind is ToolKind.EndMill or ToolKind.FaceMill or ToolKind.ChamferMill or ToolKind.EdgeFinder;
            if (Kind == MachineKind.Lathe ? millOnly : t.IsLatheTool)
                problems.Add($"T{t.Number} ({t.Kind}) is not a {Kind.ToString().ToLowerInvariant()} tool.");
            if (t.LengthMm <= 0f || (!t.IsLatheTool && t.DiameterMm <= 0f))
                problems.Add($"T{t.Number}: length and diameter must be positive.");
        }

        if (Kind == MachineKind.Mill && Mill != null)
        {
            if (Mill.StockSizeXMm <= 0f || Mill.StockSizeYMm <= 0f || Mill.StockHeight <= 0f || Mill.VoxelStepMm <= 0f)
                problems.Add("Stock sizes and voxel step must be positive.");
            if (Mill.StockSizeXMm > Mill.TableSizeXMm || Mill.StockSizeYMm > Mill.TableSizeYMm)
                problems.Add("The stock is larger than the table.");
        }
        if (Kind == MachineKind.Lathe && Lathe != null)
        {
            if (Lathe.StockDiameterMm <= 0f || Lathe.StockLengthMm <= Lathe.GripLengthMm)
                problems.Add("Stock must be longer than the length gripped by the chuck.");
            if (Lathe.StockDiameterMm >= Lathe.ChuckDiameterMm)
                problems.Add("The stock is thicker than the chuck.");
            if (Lathe.Turret == TurretKind.Disc && Lathe.TurretStations < tools.Length)
                problems.Add($"The turret has {Lathe.TurretStations} stations for {tools.Length} tools.");
        }
        return problems;
    }

    /// <summary>Changes whenever any value in the spec or its
    /// sub-resources does.</summary>
    public string Fingerprint()
    {
        var sb = new StringBuilder();
        AppendFingerprint(this, sb);
        return sb.ToString();
    }

    private static void AppendFingerprint(Variant value, StringBuilder sb)
    {
        if (value.VariantType == Variant.Type.Array)
        {
            foreach (var item in value.AsGodotArray())
                AppendFingerprint(item, sb);
        }
        else if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Resource resource)
        {
            foreach (var property in resource.GetPropertyList())
                if ((property["usage"].AsInt32() & (int)PropertyUsageFlags.ScriptVariable) != 0)
                    AppendFingerprint(resource.Get(property["name"].AsStringName()), sb);
        }
        else
            sb.Append(value.ToString()).Append('|');
    }
}
