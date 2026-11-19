using System.Linq;
using System.Text;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Spec;
using CncTrainer.Workshop;
using static System.FormattableString;

namespace CncTrainer.Inspection;

/// <summary>
/// A job as the operator gets it: what to make, from which stock, with
/// which program, and the drawing's toleranced dimensions. Picked on the
/// work order board; the inspection table grades the finished part against
/// it. Saved as .tres under res://Machines/WorkOrders/, where the board finds
/// every order on its own.
/// </summary>
[Tool, GlobalClass]
public partial class WorkOrder : Resource
{
    /// <summary>Part name shown on the board and the drawing card.</summary>
    [Export] public string Title = "Yeni iş";
    /// <summary>Order number on the paperwork.</summary>
    [Export] public string OrderNumber = "İE-0001";
    /// <summary>Which kind of machine makes it (the drawing says where to go).</summary>
    [Export] public MachineKind Machine = MachineKind.Lathe;
    /// <summary>Material the part must be made of; another material fails it.</summary>
    [Export] public MaterialKind Material = MaterialKind.Aluminum;
    /// <summary>Raw stock shape to take from the stock station.</summary>
    [Export] public StockShape StockShape = StockShape.CylinderAlongX;
    /// <summary>Raw stock size: block = length x width x height; round bar =
    /// length x diameter x diameter (the stock station's convention).</summary>
    [Export] public Vector3 StockSizeMm = new(80f, 50f, 50f);
    /// <summary>Program to run, under linuxcnc-config/ (e.g. nc_files/lathe_kademeli_mil.ngc).</summary>
    [Export] public string Program = "";
    /// <summary>Extra instructions printed on the drawing card.</summary>
    [Export(PropertyHint.MultilineText)] public string Notes = "";
    /// <summary>The drawing's toleranced dimensions.</summary>
    [Export] public Godot.Collections.Array<PartFeature> Features = new();
    /// <summary>On: a crash, a thrown part or a broken tool while the job was
    /// running fails the part even when it measures good. Off: only the
    /// dimensions and the material count; incidents are still listed.</summary>
    [Export] public bool IncidentsFailThePart = true;

    public StockBlank Stock => new(StockShape, StockSizeMm, Material);

    public FeatureSpec[] FeatureSpecs() => Features.Where(f => f != null).Select(f => f.ToSpec()).ToArray();

    /// <summary>The drawing card on the HUD while the job is open.</summary>
    public string DrawingText()
    {
        var sb = new StringBuilder();
        sb.Append($"İŞ EMRİ {OrderNumber}: {Title}\n");
        sb.Append($"Tezgah: {(Machine == MachineKind.Lathe ? "Torna" : "Freze")}   Malzeme: {StockBlank.NameOf(Material)}\n");
        sb.Append($"Ham: {Stock.Describe()}\n");
        if (!string.IsNullOrWhiteSpace(Program))
            sb.Append($"Program: {Program}\n");
        sb.Append("ÖLÇÜLER\n");
        int n = 1;
        foreach (var f in FeatureSpecs())
            sb.Append(Invariant($" {n++,2}. {f.Label,-18} {f.NominalMm,8:0.00}  {f.Tolerance}   {Where(f)}\n"));
        if (!string.IsNullOrWhiteSpace(Notes))
            sb.Append(Notes.Trim()).Append('\n');
        sb.Append("Bitince parçayı kalite kontrol masasına getir.");
        return sb.ToString();
    }

    private static string Where(FeatureSpec f) => f.Kind switch
    {
        FeatureKind.OutsideDiameter or FeatureKind.BoreDiameter => Invariant($"(referans uçtan {f.PositionMm.X:0.#} mm)"),
        FeatureKind.OverallLength => "(toplam boy)",
        FeatureKind.Roughness => "(µm, en çok)",
        _ => "",
    };
}
