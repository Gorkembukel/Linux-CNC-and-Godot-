using Godot;
using CncTrainer.Player;

namespace CncTrainer.UI;

/// <summary>Esc in the workshop: back to work, save / load, settings (the
/// instructor's workshop rules among them), main menu, quit. The machines
/// keep running behind it, as they would in a real shop.</summary>
public partial class PauseMenu : CanvasLayer
{
    public Main Workshop;

    private Control _root, _main;
    private SettingsPanel _settings;
    private SavePanel _saves;

    public override void _Ready()
    {
        Layer = 50;
        _root = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f), Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var box = MenuUi.CentredBox(_root, new Vector2(420f, 0f));

        _main = new VBoxContainer();
        box.AddChild(_main);
        MenuUi.Heading(_main, "Duraklatıldı");
        _main.AddChild(new Label { Text = "Makineler arkada çalışmaya devam eder." });
        MenuUi.Button(_main, "Devam", Close);
        MenuUi.Button(_main, "Kaydet / Yükle", () => Show(_saves));
        MenuUi.Button(_main, "Ayarlar ve eğitmen", () => Show(_settings));
        MenuUi.Button(_main, "Ana menü", () => Workshop.Leave(toMenu: true), "Atölye otomatik kayda yazılır.");
        MenuUi.Button(_main, "Çık", () => Workshop.Leave(toMenu: false), "Atölye otomatik kayda yazılır.");

        _settings = new SettingsPanel { Visible = false };
        _settings.Closed += () => Show(_main);
        box.AddChild(_settings);
        _saves = new SavePanel { Visible = false, Write = Workshop.Write };
        _saves.Closed += () => Show(_main);
        box.AddChild(_saves);
    }

    private void Show(Control page)
    {
        _main.Visible = page == _main;
        _settings.Visible = page == _settings;
        _saves.Visible = page == _saves;
    }

    private PlayerController Player => GetTree().GetFirstNodeInGroup("player") as PlayerController;

    // Unhandled: a console in close-up or an open panel takes Esc first.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;
        if (_root.Visible)
            Close();
        else if (Input.MouseMode == Input.MouseModeEnum.Captured)
            Open();
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    private void Open()
    {
        Show(_main);
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (Player is { } player)
            player.Suspended = true;
    }

    private void Close()
    {
        _root.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        if (Player is { } player)
            player.Suspended = false;
    }
}
