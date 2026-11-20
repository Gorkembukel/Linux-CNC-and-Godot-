using System.Collections.Generic;
using System.Linq;

namespace CncTrainer.Bridge;

/// <summary>One line of the control's tool offset page.</summary>
public readonly record struct ToolOffsetRow(int Number, float GeometryX, float WearX, float GeometryZ, float WearZ, float Diameter, int Orientation)
{
    public float Get(AxisId axis, bool wear) => axis == AxisId.X ? (wear ? WearX : GeometryX) : (wear ? WearZ : GeometryZ);
}

/// <summary>
/// The tool offset memory of a Fanuc-style control on top of LinuxCNC's tool
/// table. LinuxCNC keeps one offset per axis and tool, which is what G43
/// applies; the control splits it into geometry + wear the way an operator
/// sees it. The table comes from the bridge (TOOL_TABLE packets), the wear
/// lives here, and geometry = LinuxCNC's offset - wear. Wear lasts as long as
/// the machine's LinuxCNC does: both start over with the session.
/// Engine-free.
/// </summary>
public sealed class ToolOffsetTable
{
    private readonly SortedDictionary<int, ToolTableEntry> _control = new();
    private readonly Dictionary<(int Tool, AxisId Axis), float> _wear = new();

    public bool Received { get; private set; }

    public void Update(IEnumerable<ToolTableEntry> entries)
    {
        _control.Clear();
        foreach (var entry in entries)
            _control[entry.Number] = entry;
        Received = true;
    }

    public IReadOnlyList<ToolOffsetRow> Rows => _control.Values.Select(e => new ToolOffsetRow(e.Number,
        (float)e.X - Wear(e.Number, AxisId.X), Wear(e.Number, AxisId.X),
        (float)e.Z - Wear(e.Number, AxisId.Z), Wear(e.Number, AxisId.Z),
        (float)e.Diameter, e.Orientation)).ToList();

    public bool Has(int tool) => _control.ContainsKey(tool);

    /// <summary>The table as LinuxCNC holds it (wear included).</summary>
    public IEnumerable<ToolTableEntry> Entries => _control.Values;

    public IEnumerable<(int Tool, AxisId Axis, float Wear)> Wears => _wear.Select(w => (w.Key.Tool, w.Key.Axis, w.Value));

    /// <summary>A wear value from a save (LinuxCNC already holds the sum).</summary>
    public void RestoreWear(int tool, AxisId axis, float wear) => _wear[(tool, axis)] = wear;

    public float Wear(int tool, AxisId axis) => _wear.TryGetValue((tool, axis), out float w) ? w : 0f;

    /// <summary>The offset LinuxCNC must hold so the geometry reads `geometry`.</summary>
    public float TotalForGeometry(int tool, AxisId axis, float geometry) => geometry + Wear(tool, axis);

    /// <summary>Sets the wear and returns the offset LinuxCNC must now hold
    /// (the geometry stays), or null for a tool not in the table.</summary>
    public float? SetWear(int tool, AxisId axis, float wear)
    {
        if (!_control.TryGetValue(tool, out var entry))
            return null;
        float total = (float)(axis == AxisId.X ? entry.X : entry.Z) - Wear(tool, axis) + wear;
        _wear[(tool, axis)] = wear;
        return total;
    }
}
