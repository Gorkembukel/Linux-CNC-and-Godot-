using static System.FormattableString;

namespace CncTrainer.Interaction;

/// <summary>Something of known thickness slid between the tool tip and the
/// work to set a tool's Z: a slip of paper, or a setting block. It drags
/// when the gap matches its thickness within FitMm. Engine-free.</summary>
public readonly record struct FeelerGauge(string Name, float ThicknessMm, float FitMm, string TooTight)
{
    /// <summary>The feeler a hand tool is, or null for any other tool.</summary>
    public static FeelerGauge? For(HandToolKind kind) => kind switch
    {
        HandToolKind.PaperGauge => new FeelerGauge("Kağıt mastar", 0.05f, 0.03f, "KAĞIT YIRTILDI, takım yüzeye bastı (yukarı çık)"),
        HandToolKind.GaugeBlock => new FeelerGauge("Ayar bloğu", 50f, 0.01f, "blok girmiyor, takım çok alçakta (yukarı çık)"),
        _ => null,
    };

    /// <summary>The largest gap worth looking for under the tool (mm).</summary>
    public float SearchMm => ThicknessMm + 2f;

    /// <summary>What the operator feels with the tip this far above the
    /// surface (null = nothing under it within reach).</summary>
    public string Feel(float? gapMm) =>
        gapMm is not float gap ? $"{Name}: takım yüzeyden uzak"
        : gap < ThicknessMm - FitMm ? $"{Name}: {TooTight}"
        : gap <= ThicknessMm + FitMm ? Invariant($"{Name}: sıkı geçiyor  ->  takım ucu yüzeyin {ThicknessMm:0.000} mm üstünde")
        : $"{Name}: serbest kayıyor (daha in)";
}
