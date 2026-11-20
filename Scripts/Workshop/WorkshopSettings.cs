using System.Collections.Generic;
using Godot;
using CncTrainer.Settings;

namespace CncTrainer.Workshop;

/// <summary>Workshop rules that are a choice, not physics. Defaults in
/// Project Settings (section cnc_trainer/workshop); the instructor panel
/// overrides them per player (user://settings.cfg).</summary>
public static class WorkshopSettings
{
    public const string SawRequiresClampingKey = "cnc_trainer/workshop/saw_requires_clamping";
    public const string PhysicalHandlingKey = "cnc_trainer/workshop/physical_handling";

    private static readonly Setting SawClamping = new(SawRequiresClampingKey, "Testere için bağlama şart",
        "Açık: sadece mengene, ayna ya da bağlama aparatında tutulan kütük kesilir. Kapalı: serbest kütük de kesilir.", false);
    private static readonly Setting Physical = new(PhysicalHandlingKey, "Fiziksel kullanım",
        "Açık: taşınan şeyler sınırlı el gücüyle çekilir, çeneler sehpada tek tek elle takılır, ekranda doğru/yanlış yazmaz. Kapalı: taşınanlar bakışı birebir izler, yazılı ipuçları çıkar.", true);
    private static readonly Setting HandStrength = new("cnc_trainer/workshop/hand_strength_n", "El gücü (N)",
        "Ellerin taşınan şeyi ne kadar hızlı savurabildiği. Ağırlığı her zaman taşınır; bu sadece ne kadar çabuk izlediğini sınırlar.", 260f, 60f, 800f, 10f);
    private static readonly Setting Weight = new("cnc_trainer/workshop/weight_feel", "Ağırlık hissi",
        "Taşınan şeyin kütlesinin ne kadarı ellerde hissedilir (0 = ağırlıksız, 1 = tamamı): ağır aynalar gecikir ve sallanır.", 0.35f, 0f, 1f, 0.05f);
    private static readonly Setting Interlocks = new("cnc_trainer/workshop/door_interlocks", "Kapı kilitleri",
        "Açık: kapı kilidi olan makineler kapı açıkken çalışmaz. Kapalı: eğitmen kilitleri devreden çıkarır (kapı açık çalışma gösterimi için).", true);
    private static readonly Setting Glasses = new("cnc_trainer/workshop/glasses_rule", "Gözlük kuralı kaydı",
        "Açık: tezgah keserken yakında gözlüksüz durmak güvenlik kaydına geçer ve işin karnesine yazılır.", true);
    private static readonly Setting Wear = new("cnc_trainer/workshop/tool_wear_scale", "Takım aşınma hızı",
        "1 = gerçek takım ömrü. Daha büyük değerde takımlar o kat hızlı aşınır (aşınmayı kısa derste göstermek için).", 1f, 0.5f, 30f, 0.5f);
    private static readonly Setting Chips = new("cnc_trainer/workshop/chip_visual_scale", "Talaş görünür boyu",
        "Talaşlar gerçek boylarının bu katı çizilir ki görülebilsinler.", 6f, 1f, 12f, 0.5f);
    private static readonly Setting Belt = new("cnc_trainer/workshop/belt_slots", "Kemer yuvası",
        "Alet kemerindeki kılıf sayısı.", 8f, 4f, 12f, 1f, NextSession: true);

    public static IReadOnlyList<Setting> All { get; } = new[] { Physical, HandStrength, Weight, SawClamping, Interlocks, Glasses, Wear, Chips, Belt };

    /// <summary>Only stock held in a vise, chuck or fixture can be sawn (off: loose stock too).</summary>
    public static bool SawRequiresClamping => SettingsStore.Bool(SawClamping);

    /// <summary>On: what the player carries is pulled along by the hands
    /// with limited strength, and chucks are put together on the chuck stand
    /// jaw by jaw, with nothing on screen saying what is right. Off: carried
    /// things follow the view exactly, with text hints.</summary>
    public static bool PhysicalHandling => SettingsStore.Bool(Physical);

    /// <summary>How hard the player's hands can swing what they carry about (N).</summary>
    public static float HandStrengthN => SettingsStore.Float(HandStrength);

    /// <summary>Share of a carried thing's mass felt as swing (0..1).</summary>
    public static float WeightFeel => Mathf.Clamp(SettingsStore.Float(Weight), 0f, 1f);

    /// <summary>Off: no machine's door interlock is active.</summary>
    public static bool DoorInterlocks => SettingsStore.Bool(Interlocks);

    /// <summary>Standing by a cutting machine without glasses is recorded.</summary>
    public static bool GlassesRule => SettingsStore.Bool(Glasses);

    /// <summary>Multiplies tool wear (1 = real tool life).</summary>
    public static float ToolWearScale => Mathf.Max(0.01f, SettingsStore.Float(Wear));

    /// <summary>Chips are drawn this many times their real size.</summary>
    public static float ChipVisualScale => SettingsStore.Float(Chips);

    public static int BeltSlots => Mathf.Clamp(Mathf.RoundToInt(SettingsStore.Float(Belt)), 2, 16);
}
