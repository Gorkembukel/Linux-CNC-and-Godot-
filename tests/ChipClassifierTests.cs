using CncTrainer.Chips;

namespace CncTrainer.Tests;

public class ChipClassifierTests
{
    [Fact]
    public void TinyChipLoad_AlwaysProducesBrokenChips()
    {
        var f = ChipClassifier.Classify(MaterialPreset.Aluminum, depthOfCutMm: 0.01f, feedPerRevMm: 0.01f, cuttingSpeedMmPerSec: 1500f);
        Assert.Equal(ChipType.Broken, f.Type);
    }

    [Fact]
    public void MoreDuctileMaterial_IsMoreLikelyToFormContinuousChips_AtTheSameCuttingParameters()
    {
        // The brief's actual bar: "the effect of cutting parameters on the chip
        // ... must go in the right direction" -- ductility is the clearest case.
        var aluminum = ChipClassifier.Classify(MaterialPreset.Aluminum, 0.3f, 0.2f, 1500f); // ductility 0.85
        var brass = ChipClassifier.Classify(MaterialPreset.Brass, 0.3f, 0.2f, 1500f); // ductility 0.35

        Assert.True((int)aluminum.Type >= (int)brass.Type,
            $"aluminum ({aluminum.Type}) should form at least as continuous a chip as brass ({brass.Type}) at identical cut parameters");
    }

    [Fact]
    public void HigherChipLoad_AtFixedMaterialAndSpeed_MovesTowardContinuous()
    {
        var light = ChipClassifier.Classify(MaterialPreset.Steel, 0.05f, 0.05f, 1500f);
        var heavy = ChipClassifier.Classify(MaterialPreset.Steel, 1.0f, 0.5f, 1500f);

        Assert.True((int)heavy.Type >= (int)light.Type);
        Assert.True(heavy.SizeMm > light.SizeMm);
    }

    [Fact]
    public void Steel_HeatTintsTowardStrawThenBlue_AsCuttingSpeedIncreases_ButOtherMaterialsDoNot()
    {
        var steelSlow = ChipClassifier.Classify(MaterialPreset.Steel, 0.3f, 0.2f, 800f); // below reference speed
        var steelFast = ChipClassifier.Classify(MaterialPreset.Steel, 0.3f, 0.2f, 4000f); // well above reference speed
        var aluminumFast = ChipClassifier.Classify(MaterialPreset.Aluminum, 0.3f, 0.2f, 4000f);

        Assert.Equal(MaterialPreset.Steel.BaseColor, steelSlow.Color); // no heat effect below the reference speed
        Assert.NotEqual(MaterialPreset.Steel.BaseColor, steelFast.Color); // heat-tinted at high speed
        Assert.Equal(MaterialPreset.Aluminum.BaseColor, aluminumFast.Color); // aluminum never heat-tints
    }

    [Fact]
    public void SpawnRate_NeverNegative_AndRisesWithChipLoad()
    {
        var f1 = ChipClassifier.Classify(MaterialPreset.Steel, 0.05f, 0.05f, 1000f);
        var f2 = ChipClassifier.Classify(MaterialPreset.Steel, 0.8f, 0.4f, 1000f);

        Assert.True(f1.SpawnRatePerSecond >= 0f);
        Assert.True(f2.SpawnRatePerSecond >= f1.SpawnRatePerSecond);
    }
}
