using Godot;

namespace CncTrainer;

/// <summary>The control's LCD as a 2D UI, drawn into the console's
/// SubViewport: title bar, page body, key-in line, status bar and the ten
/// softkey labels that sit right above the physical softkeys.</summary>
[Tool]
public partial class ConsoleScreen : Control
{
    public const int Width = 640;
    public const int Height = 480;
    public const int SoftkeyCount = 10;

    private static readonly Color Background = new(0.03f, 0.05f, 0.12f);
    private static readonly Color TitleBar = new(0.13f, 0.27f, 0.52f);
    private static readonly Color TextColor = new(0.92f, 0.94f, 0.97f);
    private static readonly Color Accent = new(0.45f, 0.85f, 1f);
    private static readonly Color KeyIn = new(1f, 0.86f, 0.3f);

    private Label _title, _titleRight, _big, _body, _keyIn, _status;
    private readonly Label[] _softkeys = new Label[SoftkeyCount];
    private readonly ColorRect[] _softkeyBoxes = new ColorRect[SoftkeyCount];

    public ConsoleScreen()
    {
        Size = new Vector2(Width, Height);
        var font = new SystemFont { FontNames = new[] { "Consolas", "Lucida Console", "DejaVu Sans Mono", "Courier New", "monospace" } };

        AddChild(new ColorRect { Color = Background, Size = Size });
        AddChild(new ColorRect { Color = TitleBar, Size = new Vector2(Width, 34) });
        _title = MakeLabel(font, 20, TextColor, new Vector2(10, 4), new Vector2(380, 28));
        _titleRight = MakeLabel(font, 18, TextColor, new Vector2(390, 5), new Vector2(240, 28));
        _titleRight.HorizontalAlignment = HorizontalAlignment.Right;

        _big = MakeLabel(font, 38, TextColor, new Vector2(18, 44), new Vector2(Width - 36, 230));
        _body = MakeLabel(font, 18, TextColor, new Vector2(18, 44), new Vector2(Width - 36, 330));

        AddChild(new ColorRect { Color = new Color(0.25f, 0.3f, 0.4f), Position = new Vector2(0, 378), Size = new Vector2(Width, 1) });
        _keyIn = MakeLabel(font, 20, KeyIn, new Vector2(10, 382), new Vector2(Width - 20, 26));
        _status = MakeLabel(font, 17, Accent, new Vector2(10, 412), new Vector2(Width - 20, 24));

        float slot = Width / (float)SoftkeyCount;
        for (int i = 0; i < SoftkeyCount; i++)
        {
            _softkeyBoxes[i] = new ColorRect { Position = new Vector2(i * slot + 3, 442), Size = new Vector2(slot - 6, 34) };
            AddChild(_softkeyBoxes[i]);
            _softkeys[i] = MakeLabel(font, 13, Colors.Black, new Vector2(i * slot + 3, 449), new Vector2(slot - 6, 24));
            _softkeys[i].HorizontalAlignment = HorizontalAlignment.Center;
        }
        SetSoftkeys(new string[SoftkeyCount]);
        Render("", "", "", "(live data at runtime)", "", "");
    }

    private Label MakeLabel(Font font, int size, Color color, Vector2 position, Vector2 boxSize)
    {
        var label = new Label { Position = position, Size = boxSize, ClipText = true };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
        return label;
    }

    /// <summary>One frame of the screen. `big` is for the large position
    /// digits; `body` is drawn below it (or from the top when `big` is empty).</summary>
    public void Render(string title, string titleRight, string big, string body, string keyIn, string status)
    {
        _title.Text = title;
        _titleRight.Text = titleRight;
        _big.Text = big;
        _body.Position = new Vector2(18, big.Length == 0 ? 44 : 44 + 46 * (big.Split('\n').Length));
        _body.Text = body;
        _keyIn.Text = ">" + keyIn + "_";
        _status.Text = status;
    }

    public void SetSoftkeys(string[] labels)
    {
        for (int i = 0; i < SoftkeyCount; i++)
        {
            string text = i < labels.Length ? labels[i] ?? "" : "";
            _softkeys[i].Text = text;
            _softkeyBoxes[i].Color = text.Length > 0 ? new Color(0.78f, 0.8f, 0.83f) : new Color(0.14f, 0.16f, 0.22f);
        }
    }
}
