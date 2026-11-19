using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Player;

/// <summary>The job's drawing on the HUD: the sheet scaled into this
/// control, its notes written over it at the same scale.</summary>
public partial class DrawingView : Control
{
    private JobDrawing _drawing;

    public JobDrawing Drawing
    {
        get => _drawing;
        set
        {
            if (ReferenceEquals(value, _drawing))
                return;
            _drawing = value;
            Visible = value != null;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_drawing?.Sheet is not Texture2D sheet)
            return;
        var size = sheet.GetSize();
        float scale = Mathf.Min(Size.X / size.X, Size.Y / size.Y);
        var origin = new Vector2(0f, Size.Y - size.Y * scale);
        DrawTextureRect(sheet, new Rect2(origin, size * scale), false);
        var font = ThemeDB.FallbackFont;
        foreach (var (at, text, px) in _drawing.Notes)
            DrawString(font, origin + at * scale, text, HorizontalAlignment.Left, -1f,
                Mathf.Max(6, Mathf.RoundToInt(px * scale)), Colors.Black);
    }
}
