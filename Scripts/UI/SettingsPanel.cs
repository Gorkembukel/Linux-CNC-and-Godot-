using System;
using System.Collections.Generic;
using Godot;
using CncTrainer.Settings;
using CncTrainer.Workshop;

namespace CncTrainer.UI;

/// <summary>The settings screen, drawn from the setting lists themselves:
/// the player's own preferences and the instructor's workshop rules.</summary>
public partial class SettingsPanel : VBoxContainer
{
    public event Action Closed;

    public override void _Ready()
    {
        MenuUi.Heading(this, "Ayarlar");
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(820f, 440f), SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(tabs);
        AddPage(tabs, "Oyun", GameSettings.All);
        AddPage(tabs, "Eğitmen", WorkshopSettings.All);
        MenuUi.Button(this, "Geri", () => Closed?.Invoke());
    }

    private static void AddPage(TabContainer tabs, string name, IEnumerable<Setting> settings)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // Label, control, value, reset: one column each so the rows line up.
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("v_separation", 10);
        grid.AddThemeConstantOverride("h_separation", 14);
        scroll.AddChild(grid);
        tabs.AddChild(scroll);
        foreach (var setting in settings)
            AddRow(grid, setting);
    }

    private static void AddRow(GridContainer grid, Setting s)
    {
        string tip = s.Tooltip + (s.NextSession ? "\n(Yeni oyunda geçerli olur.)" : "");
        grid.AddChild(new Label { Text = s.Label, CustomMinimumSize = new Vector2(240f, 0f), TooltipText = tip, MouseFilter = MouseFilterEnum.Pass });
        Action show;
        if (s.IsToggle)
        {
            var check = new CheckButton { TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            check.Toggled += on => SettingsStore.Set(s, on);
            grid.AddChild(check);
            grid.AddChild(new Control());
            show = () => check.SetPressedNoSignal(SettingsStore.Bool(s));
        }
        else
        {
            var slider = new HSlider
            {
                MinValue = s.Min, MaxValue = s.Max, Step = s.Step, TooltipText = tip, CustomMinimumSize = new Vector2(260f, 0f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var value = new Label { CustomMinimumSize = new Vector2(70f, 0f) };
            string format = s.Step >= 1f ? "0" : s.Step >= 0.1f ? "0.0" : s.Step >= 0.01f ? "0.00" : "0.0000";
            slider.ValueChanged += v =>
            {
                value.Text = v.ToString(format);
                SettingsStore.Set(s, (float)v);
            };
            grid.AddChild(slider);
            grid.AddChild(value);
            show = () =>
            {
                slider.SetValueNoSignal(SettingsStore.Float(s));
                value.Text = slider.Value.ToString(format);
            };
        }
        var reset = new Button { Text = "Varsayılan", TooltipText = "Atölyenin varsayılanına döndür" };
        reset.Pressed += () =>
        {
            SettingsStore.Reset(s);
            show();
        };
        grid.AddChild(reset);
        show();
    }
}
