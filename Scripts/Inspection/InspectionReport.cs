using System.Collections.Generic;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace CncTrainer.Inspection;

/// <summary>The report card for one part against its work order. Engine-free.</summary>
public sealed record InspectionReport(
    string OrderTitle, IReadOnlyList<FeatureResult> Features, bool MaterialOk, string MaterialNote,
    IReadOnlyList<string> Incidents, bool IncidentsFail, float SlipMm = 0f, float SpringMm = 0f)
{
    /// <summary>Creep below this does not explain a size that is off.</summary>
    public const float SignificantSlipMm = 0.02f;
    /// <summary>A tool that sprang off the work less than this did not make a size miss.</summary>
    public const float SignificantSpringMm = 0.01f;

    public int FeaturesPassed => Features.Count(f => f.Pass);

    public bool Pass => MaterialOk && Features.All(f => f.Pass) && !(IncidentsFail && Incidents.Count > 0);

    /// <summary>0-100: dimensions weigh most, the material and a clean run the rest.</summary>
    public int Score
    {
        get
        {
            float dims = Features.Count == 0 ? 1f : FeaturesPassed / (float)Features.Count;
            float score = 70f * dims + (MaterialOk ? 15f : 0f) + (Incidents.Count == 0 ? 15f : 0f);
            return (int)System.MathF.Round(score);
        }
    }

    public string Text()
    {
        var sb = new StringBuilder();
        sb.Append($"KARNE: {OrderTitle}   {(Pass ? "KABUL" : "RED")}   puan {Score}/100\n");
        sb.Append(MaterialOk ? "Malzeme: doğru\n" : $"Malzeme: YANLIŞ ({MaterialNote})\n");
        int n = 1;
        foreach (var f in Features)
        {
            string measured = f.MeasuredMm is float v ? Invariant($"{v,8:0.000}") : "  ölçülemedi";
            string deviation = f.MeasuredMm is float m ? Invariant($"{m - f.Spec.NominalMm:+0.000;-0.000; 0.000}") : "";
            sb.Append(Invariant($" {n++,2}. {f.Spec.Label,-18} {f.Spec.NominalMm,8:0.00} {f.Spec.Tolerance,-12} {measured} {deviation,7}  {(f.Pass ? "OK" : "HATA")}\n"));
        }
        if (Incidents.Count == 0)
            sb.Append("Olay: yok\n");
        else
        {
            sb.Append($"Olaylar ({Incidents.Count}){(IncidentsFail ? ": parçayı reddettirir" : "")}\n");
            foreach (var incident in Incidents.Take(6))
                sb.Append(" - ").Append(incident.Split('\n')[0]).Append('\n');
        }
        sb.Append(Pass ? "Parça kabul edildi." : Hint());
        return sb.ToString();
    }

    /// <summary>Where to look first, the way an inspector would point at it.</summary>
    private string Hint()
    {
        if (!MaterialOk)
            return "İş emrindeki malzemeyle yeniden başla.";
        var bad = Features.FirstOrDefault(f => !f.Pass);
        if (bad.MeasuredMm != null && SlipMm >= SignificantSlipMm)
            return Invariant($"Kök neden: parça kesim sırasında bağlamada {SlipMm:0.00} mm kaydı (tam sıkılmamış). Mengeneyi / pabuçları tam sık, yeni parça.");
        if (bad.MeasuredMm != null && SpringMm >= SignificantSpringMm)
            return Invariant($"Kök neden: son takım kesimde {SpringMm:0.00} mm esnedi (uzun çıkıntı ya da ağır paso). Takımı kısa bağla, son pasoyu hafif al ya da aynı ölçüye bir boş paso daha ver.");
        if (bad.MeasuredMm is float v)
        {
            float off = v - bad.Spec.NominalMm;
            return Invariant($"İlk hata: {bad.Spec.Label} {off:+0.000;-0.000} mm {(off > 0 ? "büyük" : "küçük")}. O ölçüyü kesen takımın aşınmasını (OFS/SET) düzelt, sonra yeni parça.");
        }
        if (bad.Spec.Label != null)
            return $"{bad.Spec.Label} parçada bulunamadı: program tamamlandı mı?";
        return "Olaylar yüzünden reddedildi: güvenli çalış ve tekrar dene.";
    }
}
