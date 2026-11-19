using Godot;

namespace CncTrainer.Workshop;

/// <summary>Workshop rules that are a choice, not physics: Project Settings,
/// section cnc_trainer/workshop.</summary>
public static class WorkshopSettings
{
    public const string SawRequiresClampingKey = "cnc_trainer/workshop/saw_requires_clamping";
    public const string PhysicalHandlingKey = "cnc_trainer/workshop/physical_handling";
    public const string HandStrengthKey = "cnc_trainer/workshop/hand_strength_n";

    /// <summary>Only stock held in a vise, chuck or fixture can be sawn (off: loose stock too).</summary>
    public static bool SawRequiresClamping => Get(SawRequiresClampingKey, false);

    /// <summary>On: what the player carries is pulled along by the hands
    /// with limited strength, so a heavy chuck sags and lags and can be
    /// laid into place by feel, and chucks are put together on the chuck
    /// stand jaw by jaw. Off: carried things follow the view exactly.</summary>
    public static bool PhysicalHandling => Get(PhysicalHandlingKey, true);

    /// <summary>How hard the player's hands can pull or lift (N): about
    /// 25 kg held still, less when moving it about.</summary>
    public static float HandStrengthN => Get(HandStrengthKey, 260f);

    private static T Get<[MustBeVariant] T>(string key, T fallback) =>
        ProjectSettings.HasSetting(key) ? ProjectSettings.GetSetting(key).As<T>() : fallback;
}
