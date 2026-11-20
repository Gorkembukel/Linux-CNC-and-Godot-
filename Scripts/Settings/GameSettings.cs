using System.Collections.Generic;
using Godot;

namespace CncTrainer.Settings;

/// <summary>The player's own preferences: view, sound, window.</summary>
public static class GameSettings
{
    private static readonly Setting Mouse = new("cnc_trainer/game/mouse_sensitivity", "Fare hassasiyeti",
        "Farenin bir pikseli bakışı ne kadar çevirir.", 0.0025f, 0.0005f, 0.008f, 0.0001f);
    private static readonly Setting Volume = new("cnc_trainer/game/master_volume", "Ses düzeyi",
        "Bütün seslerin düzeyi (0 = sessiz).", 0.8f, 0f, 1f, 0.05f);
    private static readonly Setting Fullscreen = new("cnc_trainer/game/fullscreen", "Tam ekran",
        "Oyun tam ekran ya da pencerede.", false);
    private static readonly Setting Autosave = new("cnc_trainer/game/autosave_minutes", "Otomatik kayıt (dk)",
        "Atölye bu kadar dakikada bir otomatik kayda yazılır (0 = sadece çıkarken).", 5f, 0f, 30f, 1f);

    public static IReadOnlyList<Setting> All { get; } = new[] { Mouse, Volume, Fullscreen, Autosave };

    /// <summary>Radians the view turns per pixel of mouse movement.</summary>
    public static float MouseSensitivity => SettingsStore.Float(Mouse);
    public static float AutosaveMinutes => SettingsStore.Float(Autosave);

    private static bool _watching;

    /// <summary>Puts sound and window settings into effect, and keeps them so.</summary>
    public static void Apply()
    {
        if (!_watching)
        {
            _watching = true;
            SettingsStore.Changed += _ => Apply();
        }
        float volume = SettingsStore.Float(Volume);
        AudioServer.SetBusVolumeDb(0, volume <= 0f ? -80f : Mathf.LinearToDb(volume));
        if (DisplayServer.GetName() == "headless")
            return;
        bool want = SettingsStore.Bool(Fullscreen);
        bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        if (want != full)
            DisplayServer.WindowSetMode(want ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
}
