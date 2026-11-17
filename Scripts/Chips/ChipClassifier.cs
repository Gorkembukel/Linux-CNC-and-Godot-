using Godot;

namespace CncTrainer.Chips;

public enum ChipType { Broken, Segmented, Continuous }

public readonly struct ChipFormation
{
    public readonly ChipType Type;
    public readonly float SpawnRatePerSecond;
    public readonly float SizeMm;
    public readonly float CurlRadiusMm;
    public readonly Color Color;
    public readonly float Gloss;

    public ChipFormation(ChipType type, float spawnRatePerSecond, float sizeMm, float curlRadiusMm, Color color, float gloss)
    {
        Type = type;
        SpawnRatePerSecond = spawnRatePerSecond;
        SizeMm = sizeMm;
        CurlRadiusMm = curlRadiusMm;
        Color = color;
        Gloss = gloss;
    }
}

/// <summary>
/// Brief section 6: turns (chip cross-section, cutting speed, material, tool
/// geometry) -- none of which LinuxCNC provides directly -- into chip
/// type/rate/size/curl/color. This is a heuristic, not a physical cutting-
/// mechanics model: the brief explicitly only asks that "the effect of cutting
/// parameters on the chip... go in the right direction", which is the bar this
/// is held to, not CAM-grade accuracy. Pure function, unit-tested in
/// Tests/ChipClassifierTests.cs for exactly that directional property.
/// </summary>
public static class ChipClassifier
{
    // mm/s; a representative mid-range turning surface speed used only to
    // normalize the speed term, not a material-specific cutting-speed table.
    private const float ReferenceSurfaceSpeedMmPerSec = 1500f;

    public static ChipFormation Classify(MaterialPreset material, float depthOfCutMm, float feedPerRevMm, float cuttingSpeedMmPerSec)
    {
        float chipLoad = Mathf.Max(0f, depthOfCutMm) * Mathf.Max(0f, feedPerRevMm); // mm^2, chip cross-section area
        float speedFactor = Mathf.Clamp(cuttingSpeedMmPerSec / ReferenceSurfaceSpeedMmPerSec, 0.05f, 2.5f);
        float formationScore = material.Ductility * Mathf.Sqrt(chipLoad) * speedFactor;

        ChipType type;
        float spawnRate, size, curlRadius;

        if (chipLoad < 0.005f || formationScore < 0.15f)
        {
            type = ChipType.Broken;
            spawnRate = Mathf.Lerp(4f, 20f, Mathf.Clamp(chipLoad * 50f, 0f, 1f));
            size = Mathf.Clamp(Mathf.Sqrt(chipLoad) * 0.8f, 0.3f, 2.0f);
            curlRadius = 0f;
        }
        else if (formationScore < 0.6f)
        {
            type = ChipType.Segmented;
            spawnRate = Mathf.Lerp(10f, 40f, Mathf.Clamp(formationScore, 0f, 1f));
            size = Mathf.Clamp(Mathf.Sqrt(chipLoad) * 1.2f, 0.5f, 3.5f);
            curlRadius = Mathf.Lerp(1.5f, 4f, Mathf.Clamp(formationScore, 0f, 1f));
        }
        else
        {
            type = ChipType.Continuous;
            float t = Mathf.Clamp(formationScore / 1.5f, 0f, 1f);
            spawnRate = Mathf.Lerp(20f, 60f, t);
            size = Mathf.Clamp(Mathf.Sqrt(chipLoad) * 1.5f, 1.0f, 6.0f);
            curlRadius = Mathf.Lerp(3f, 8f, t);
        }

        Color color = material.BaseColor;
        float gloss = material.Gloss;
        if (material.CanHeatTint && speedFactor > 1.0f)
        {
            float heat = Mathf.Clamp(speedFactor - 1.0f, 0f, 1f);
            Color straw = new(0.85f, 0.55f, 0.15f);
            Color temperBlue = new(0.25f, 0.35f, 0.65f);
            color = heat < 0.5f
                ? material.BaseColor.Lerp(straw, heat * 2f)
                : straw.Lerp(temperBlue, (heat - 0.5f) * 2f);
            gloss = Mathf.Min(1f, gloss + heat * 0.2f);
        }

        return new ChipFormation(type, spawnRate, size, curlRadius, color, gloss);
    }
}
