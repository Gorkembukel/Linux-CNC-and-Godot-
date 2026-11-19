using Godot;

namespace CncTrainer.Inspection;

/// <summary>A line of text on a work order's drawing (a dimension, a note,
/// the title block), drawn over the sheet by the HUD: Godot's SVG import
/// leaves text out.</summary>
[Tool, GlobalClass]
public partial class DrawingNote : Resource
{
    /// <summary>Where the text starts, in the drawing's own pixels (left end
    /// of its baseline, as an SVG &lt;text x y&gt;).</summary>
    [Export] public Vector2 Position;
    /// <summary>The text itself.</summary>
    [Export] public string Text = "";
    /// <summary>Font size in the drawing's own pixels.</summary>
    [Export] public int FontSizePx = 14;
}
