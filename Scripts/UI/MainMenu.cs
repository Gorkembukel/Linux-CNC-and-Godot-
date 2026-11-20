using Godot;
using CncTrainer.Save;
using CncTrainer.Settings;

namespace CncTrainer.UI;

/// <summary>The first screen: continue the last save, a new game (free
/// workshop or a training scenario), load a save, settings, quit.</summary>
public partial class MainMenu : Control
{
    private Control _main;
    private ScenarioPanel _scenarios;
    private SavePanel _saves;
    private SettingsPanel _settings;

    public override void _Ready()
    {
        GameSettings.Apply();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.1f, 0.11f, 0.12f) };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var box = MenuUi.CentredBox(this, new Vector2(460f, 0f));

        _main = new VBoxContainer();
        _main.AddThemeConstantOverride("separation", 10);
        box.AddChild(_main);
        MenuUi.Heading(_main, "CNC Operatör Eğitimi", 36);
        _main.AddChild(new Label { Text = "Torna ve freze atölyesi, LinuxCNC kontrollü" });
        var latest = SaveGame.Latest();
        var resume = MenuUi.Button(_main, "Devam et", () => Session.Start(GetTree(), new StartRequest(SaveFile: latest.Path)),
            latest != null ? $"{latest.Name}  ({latest.Time:dd.MM.yyyy HH:mm}, {latest.Scenario})" : "Henüz kayıt yok");
        resume.Disabled = latest == null;
        MenuUi.Button(_main, "Yeni oyun", () => Show(_scenarios), "Serbest atölye ya da bir eğitim senaryosu (iş emri)");
        MenuUi.Button(_main, "Kayıt yükle", () => Show(_saves));
        MenuUi.Button(_main, "Ayarlar", () => Show(_settings));
        MenuUi.Button(_main, "Çık", () => GetTree().Quit());

        _scenarios = new ScenarioPanel { Visible = false };
        _scenarios.Closed += () => Show(_main);
        box.AddChild(_scenarios);
        _saves = new SavePanel { Visible = false };
        _saves.Closed += () => Show(_main);
        box.AddChild(_saves);
        _settings = new SettingsPanel { Visible = false };
        _settings.Closed += () => Show(_main);
        box.AddChild(_settings);
    }

    private void Show(Control page)
    {
        foreach (var p in new[] { _main, _scenarios, _saves, _settings })
            p.Visible = p == page;
    }
}
