using System;
using Godot;

namespace CncTrainer.UI;

/// <summary>The few building blocks every menu page is made of.</summary>
public static class MenuUi
{
    public static readonly Color Accent = new(0.95f, 0.62f, 0.15f);

    public static Button Button(Node parent, string text, Action pressed, string tooltip = null)
    {
        var button = new Button { Text = text, TooltipText = tooltip ?? "", CustomMinimumSize = new Vector2(240f, 40f) };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    public static Label Heading(Node parent, string text, int size = 28)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Accent);
        parent.AddChild(label);
        return label;
    }

    /// <summary>A dark framed box in the middle of the screen.</summary>
    public static VBoxContainer CentredBox(Node parent, Vector2 minSize)
    {
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(centre);
        var frame = new PanelContainer { CustomMinimumSize = minSize };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.1f, 0.94f),
            BorderColor = Accent,
            BorderWidthTop = 3,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        centre.AddChild(frame);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        frame.AddChild(box);
        return box;
    }
}
