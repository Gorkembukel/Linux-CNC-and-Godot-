using Godot;

namespace CncTrainer.Chips;

/// <summary>Stock material: sets cutting force, chips, colour and density.</summary>
public enum MaterialKind
{
    /// <summary>Mild steel: tough, needs the most power, long chips at light cuts.</summary>
    Steel,
    /// <summary>Aluminium: light and easy to cut, high speeds.</summary>
    Aluminum,
    /// <summary>Brass: free-cutting, short broken chips.</summary>
    Brass,
    /// <summary>Stainless steel (304): tough and stringy, work-hardens, needs coolant.</summary>
    Stainless,
    /// <summary>Delrin (POM): an engineering plastic, very light cuts, long soft chips.</summary>
    Delrin,
}

/// <summary>
/// Brief section 6: "material profile (simple presets such as steel, aluminum,
/// brass)": what the chip classifier and the cutting-load model need, not a
/// real materials database. Cutting values are handbook mid-range figures
/// for carbide tools: mild steel, a 6000-series aluminium, free-cutting brass.
/// </summary>
public readonly struct MaterialPreset
{
    public readonly MaterialKind Kind;
    public readonly Color BaseColor;
    public readonly float Ductility; // 0..1: higher = more likely to form continuous/spiral chips
    public readonly float Gloss; // base specular roughness-inverse for the chip material
    public readonly bool CanHeatTint; // only steel visibly straw/blue/purple-tints at high cutting speed
    public readonly float DensityKgPerM3; // real published density -- used to weigh a finished part
    /// <summary>Energy to turn one mm^3 into chips (J/mm^3 = W.s/mm^3).</summary>
    public readonly float SpecificEnergyJPerMm3;
    public readonly float RecommendedSpeedMPerMin;
    /// <summary>Milling feed per tooth as a fraction of the cutter diameter.</summary>
    public readonly float ChipLoadPerDiameter;
    public readonly float TurningFeedMmPerRev;
    /// <summary>Name on the stock station and the paperwork.</summary>
    public readonly string Name;
    /// <summary>Section a hacksaw forward stroke gets through, per metre of stroke.</summary>
    public readonly float SawRateMm2PerM;
    /// <summary>Cutting it dry wears the tool fast: the operator is told to switch coolant on.</summary>
    public readonly bool NeedsCoolant;

    private MaterialPreset(MaterialKind kind, string name, Color baseColor, float ductility, float gloss, bool canHeatTint, float densityKgPerM3,
        float specificEnergy, float speed, float chipLoadPerDiameter, float turningFeed, float sawRate, bool needsCoolant)
    {
        Name = name;
        SawRateMm2PerM = sawRate;
        NeedsCoolant = needsCoolant;
        SpecificEnergyJPerMm3 = specificEnergy;
        RecommendedSpeedMPerMin = speed;
        ChipLoadPerDiameter = chipLoadPerDiameter;
        TurningFeedMmPerRev = turningFeed;
        Kind = kind;
        BaseColor = baseColor;
        Ductility = ductility;
        Gloss = gloss;
        CanHeatTint = canHeatTint;
        DensityKgPerM3 = densityKgPerM3;
    }

    public static readonly MaterialPreset Steel = new(
        MaterialKind.Steel, "Çelik", new Color(0.55f, 0.55f, 0.58f), ductility: 0.55f, gloss: 0.55f, canHeatTint: true, densityKgPerM3: 7850f,
        specificEnergy: 2.8f, speed: 130f, chipLoadPerDiameter: 0.006f, turningFeed: 0.15f, sawRate: 500f, needsCoolant: true);

    public static readonly MaterialPreset Aluminum = new(
        MaterialKind.Aluminum, "Alüminyum", new Color(0.82f, 0.83f, 0.85f), ductility: 0.85f, gloss: 0.75f, canHeatTint: false, densityKgPerM3: 2700f,
        specificEnergy: 0.7f, speed: 300f, chipLoadPerDiameter: 0.012f, turningFeed: 0.2f, sawRate: 1500f, needsCoolant: false);

    public static readonly MaterialPreset Brass = new(
        MaterialKind.Brass, "Pirinç", new Color(0.85f, 0.68f, 0.30f), ductility: 0.35f, gloss: 0.8f, canHeatTint: false, densityKgPerM3: 8500f,
        specificEnergy: 1.6f, speed: 180f, chipLoadPerDiameter: 0.01f, turningFeed: 0.15f, sawRate: 1000f, needsCoolant: false);

    public static readonly MaterialPreset Stainless = new(
        MaterialKind.Stainless, "Paslanmaz", new Color(0.68f, 0.69f, 0.71f), ductility: 0.75f, gloss: 0.7f, canHeatTint: true, densityKgPerM3: 7900f,
        specificEnergy: 3.4f, speed: 90f, chipLoadPerDiameter: 0.005f, turningFeed: 0.12f, sawRate: 350f, needsCoolant: true);

    public static readonly MaterialPreset Delrin = new(
        MaterialKind.Delrin, "Delrin", new Color(0.94f, 0.93f, 0.9f), ductility: 0.9f, gloss: 0.3f, canHeatTint: false, densityKgPerM3: 1410f,
        specificEnergy: 0.25f, speed: 250f, chipLoadPerDiameter: 0.015f, turningFeed: 0.25f, sawRate: 3000f, needsCoolant: false);

    public static MaterialPreset Get(MaterialKind kind) => kind switch
    {
        MaterialKind.Steel => Steel,
        MaterialKind.Aluminum => Aluminum,
        MaterialKind.Brass => Brass,
        MaterialKind.Stainless => Stainless,
        MaterialKind.Delrin => Delrin,
        _ => Steel,
    };
}
