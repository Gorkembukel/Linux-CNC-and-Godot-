using Godot;

namespace CncTrainer.Chips;

public enum MaterialKind { Steel, Aluminum, Brass }

/// <summary>
/// Brief section 6: "material profile (simple presets such as steel, aluminum,
/// brass)". Only the handful of properties the chip classifier actually needs,
/// not a real materials database.
/// </summary>
public readonly struct MaterialPreset
{
    public readonly MaterialKind Kind;
    public readonly Color BaseColor;
    public readonly float Ductility; // 0..1: higher = more likely to form continuous/spiral chips
    public readonly float Gloss; // base specular roughness-inverse for the chip material
    public readonly bool CanHeatTint; // only steel visibly straw/blue/purple-tints at high cutting speed

    private MaterialPreset(MaterialKind kind, Color baseColor, float ductility, float gloss, bool canHeatTint)
    {
        Kind = kind;
        BaseColor = baseColor;
        Ductility = ductility;
        Gloss = gloss;
        CanHeatTint = canHeatTint;
    }

    public static readonly MaterialPreset Steel = new(
        MaterialKind.Steel, new Color(0.55f, 0.55f, 0.58f), ductility: 0.55f, gloss: 0.55f, canHeatTint: true);

    public static readonly MaterialPreset Aluminum = new(
        MaterialKind.Aluminum, new Color(0.82f, 0.83f, 0.85f), ductility: 0.85f, gloss: 0.75f, canHeatTint: false);

    public static readonly MaterialPreset Brass = new(
        MaterialKind.Brass, new Color(0.85f, 0.68f, 0.30f), ductility: 0.35f, gloss: 0.8f, canHeatTint: false);

    public static MaterialPreset Get(MaterialKind kind) => kind switch
    {
        MaterialKind.Steel => Steel,
        MaterialKind.Aluminum => Aluminum,
        MaterialKind.Brass => Brass,
        _ => Steel,
    };
}
