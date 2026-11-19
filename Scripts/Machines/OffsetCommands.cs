using System.Globalization;
using Godot;
using CncTrainer.Bridge;
using static System.FormattableString;

namespace CncTrainer;

/// <summary>The offset-setting MDI lines both operator panels send: tool
/// touch-off (lathe X/Z, mill tool length) and work zero.</summary>
public static class OffsetCommands
{
    public static bool TryParse(string text, out float value) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Lathe: the bar was measured at `knownDiameter` after a skim
    /// cut at the current X.</summary>
    public static void TouchOffDiameter(Node owner, BridgeClient bridge, float knownDiameter)
    {
        var s = bridge.State;
        bridge.SendMdi(Invariant($"G10 L1 P{s.ToolNumber} X{s.Position.X - knownDiameter / 2f:F4}"));
        ApplyToolOffsetSoon(owner, bridge);
    }

    /// <summary>Lathe: the tip touches a face whose Z is `knownZ`.</summary>
    public static void TouchOffLatheZ(Node owner, BridgeClient bridge, float knownZ)
    {
        var s = bridge.State;
        bridge.SendMdi(Invariant($"G10 L1 P{s.ToolNumber} Z{s.Position.Z - knownZ:F4}"));
        ApplyToolOffsetSoon(owner, bridge);
    }

    /// <summary>Mill: the tool length offset becomes whatever makes the
    /// current position read as `knownZ` in the active work system.</summary>
    public static void TouchOffToolLength(Node owner, BridgeClient bridge, float knownZ)
    {
        bridge.SendMdi(Invariant($"G10 L10 P{bridge.State.ToolNumber} Z{knownZ:F4}"));
        ApplyToolOffsetSoon(owner, bridge);
    }

    /// <summary>A tool's offset on one axis (geometry + wear, what G43
    /// applies) written into LinuxCNC's table.</summary>
    public static void SetToolOffset(Node owner, BridgeClient bridge, int tool, AxisId axis, float total)
    {
        bridge.SendMdi(Invariant($"G10 L1 P{tool} {axis}{total:F4}"));
        ApplyToolOffsetSoon(owner, bridge);
    }

    /// <summary>Geometry typed into the offset page; the tool's wear stays.</summary>
    public static void SetToolGeometry(Node owner, BridgeClient bridge, int tool, AxisId axis, float geometry) =>
        SetToolOffset(owner, bridge, tool, axis, bridge.Offsets.TotalForGeometry(tool, axis, geometry));

    /// <summary>Wear typed into the offset page; the tool's geometry stays.</summary>
    public static void SetToolWear(Node owner, BridgeClient bridge, int tool, AxisId axis, float wear)
    {
        if (bridge.Offsets.SetWear(tool, axis, wear) is float total)
            SetToolOffset(owner, bridge, tool, axis, total);
    }

    /// <summary>G54 so that the current position reads as `value`.</summary>
    public static void SetWorkZero(BridgeClient bridge, AxisId axis, float value) =>
        bridge.SendMdi(Invariant($"G10 L20 P1 {axis}{value:F4}"));

    /// <summary>G54 offset typed in directly.</summary>
    public static void SetWorkOffset(BridgeClient bridge, AxisId axis, float value) =>
        bridge.SendMdi(Invariant($"G10 L2 P1 {axis}{value:F4}"));

    // G10 L1/L10 only rewrite the tool table; the new offset takes effect
    // (and shows in the work coordinates) at the next G43.
    private static void ApplyToolOffsetSoon(Node owner, BridgeClient bridge) =>
        owner.GetTree().CreateTimer(0.6).Timeout += () => bridge.SendMdi("G43");
}
