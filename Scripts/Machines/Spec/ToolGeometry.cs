using System.Linq;
using Godot;
using CncTrainer.Material;

namespace CncTrainer.Spec;

/// <summary>
/// The real shape of a tool's cutting part, in the units a catalogue uses.
/// Lathe tools: the insert (standard ISO shapes or a drawn outline), and how
/// the holder presents it. Mill form cutters: the cutter's profile, radius
/// against height from the tip, revolved about the spindle axis. A ToolSpec
/// without one keeps the simple point-and-nose-radius model.
/// </summary>
[Tool, GlobalClass]
public partial class ToolGeometry : Resource
{
    [ExportGroup("Lathe insert")]
    /// <summary>Insert shape: ISO C 80°, D 55°, V 35°, T 60°, S 90°, W 80°, round, grooving blade, 60° threading, or Custom (drawn in the Tool Designer).</summary>
    [Export] public InsertShape Insert = InsertShape.C80;
    /// <summary>Cutting edge length (round inserts: diameter).</summary>
    [Export] public float EdgeLengthMm = 12f;
    [Export] public float NoseRadiusMm = 0.8f;
    [Export] public float ThicknessMm = 4.76f;
    /// <summary>Between the main cutting edge and the feed toward the chuck.</summary>
    [Export(PropertyHint.Range, "45,120,0.5")] public float ApproachDeg = 95f;
    [Export] public ToolHand Hand = ToolHand.Right;
    /// <summary>Grooving / parting blades: width; threading: unused.</summary>
    [Export] public float WidthMm = 3f;
    /// <summary>Grooving: how deep the blade reaches; threading: insert height.</summary>
    [Export] public float DepthMm = 20f;
    [Export] public float ThreadAngleDeg = 60f;
    /// <summary>Custom insert outline, (x radial, z axial) mm.</summary>
    [Export] public Vector2[] OutlineMm = System.Array.Empty<Vector2>();
    [Export] public Color InsertColor = new(0.85f, 0.7f, 0.25f);

    /// <summary>Mill form cutter: (radius, height above the tip) mm from the
    /// tip upward. Empty = the ToolSpec's standard cutter shape.</summary>
    [ExportGroup("Mill form cutter")]
    [Export] public Vector2[] MillProfileMm = System.Array.Empty<Vector2>();

    public InsertParams Params() => new(Insert, EdgeLengthMm, NoseRadiusMm, ApproachDeg, Hand, WidthMm, DepthMm, ThreadAngleDeg,
        OutlineMm.Length >= 3 ? OutlineMm : null);

    public bool IsThreading => Insert == InsertShape.Thread60;

    private ToolProfile _profile;
    private string _profileKey;

    /// <summary>The outline as the cutting model uses it (internalTool: a boring bar).</summary>
    public ToolProfile Profile(bool internalTool)
    {
        string key = $"{internalTool}|{Insert}|{EdgeLengthMm}|{NoseRadiusMm}|{ApproachDeg}|{Hand}|{WidthMm}|{DepthMm}|{ThreadAngleDeg}|"
            + string.Join(";", OutlineMm.Select(p => $"{p.X},{p.Y}"));
        if (key != _profileKey)
        {
            _profile = new ToolProfile(InsertOutline.Build(Params()), internalTool);
            _profileKey = key;
        }
        return _profile;
    }
}
