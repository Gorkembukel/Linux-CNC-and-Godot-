using System;
using Godot;

namespace CncTrainer.Settings;

/// <summary>One choice the player or the instructor can make: its key, how
/// the settings screen shows it, and its range. A bool default makes it a
/// switch, a number a slider.</summary>
public sealed record Setting(string Key, string Label, string Tooltip, Variant Default,
    float Min = 0f, float Max = 1f, float Step = 0.01f, bool NextSession = false)
{
    public bool IsToggle => Default.VariantType == Variant.Type.Bool;
}

/// <summary>
/// Where settings are read from: the player's own choices in
/// user://settings.cfg first, then Project Settings (the workshop's
/// defaults, set in the editor), then the built-in default.
/// </summary>
public static class SettingsStore
{
    public const string UserFile = "user://settings.cfg";
    private const string Section = "settings";
    private static ConfigFile _user;

    public static event Action<Setting> Changed;

    private static ConfigFile User
    {
        get
        {
            if (_user == null)
            {
                _user = new ConfigFile();
                _user.Load(UserFile);
            }
            return _user;
        }
    }

    public static Variant Get(Setting s) =>
        User.HasSectionKey(Section, s.Key) ? User.GetValue(Section, s.Key)
        : ProjectSettings.HasSetting(s.Key) ? ProjectSettings.GetSetting(s.Key)
        : s.Default;

    public static bool Bool(Setting s) => Get(s).AsBool();
    public static float Float(Setting s) => Get(s).VariantType == Variant.Type.Bool ? 0f : (float)Get(s).AsDouble();

    public static void Set(Setting s, Variant value)
    {
        User.SetValue(Section, s.Key, value);
        User.Save(UserFile);
        Changed?.Invoke(s);
    }

    /// <summary>Back to the workshop default.</summary>
    public static void Reset(Setting s)
    {
        if (User.HasSectionKey(Section, s.Key))
            User.EraseSectionKey(Section, s.Key);
        User.Save(UserFile);
        Changed?.Invoke(s);
    }
}
