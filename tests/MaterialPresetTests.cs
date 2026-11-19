using CncTrainer.Chips;

namespace Tests;

public class MaterialPresetTests
{
    [Fact]
    public void EveryMaterial_HasItsOwnPreset()
    {
        foreach (var kind in Enum.GetValues<MaterialKind>())
        {
            var preset = MaterialPreset.Get(kind);
            Assert.Equal(kind, preset.Kind);
            Assert.False(string.IsNullOrEmpty(preset.Name));
            Assert.True(preset.SawRateMm2PerM > 0f);
        }
    }

    [Fact]
    public void Stainless_IsHarderToCutThanMildSteel_AndDelrinTheEasiest()
    {
        Assert.True(MaterialPreset.Stainless.SpecificEnergyJPerMm3 > MaterialPreset.Steel.SpecificEnergyJPerMm3);
        Assert.True(MaterialPreset.Stainless.NeedsCoolant);
        Assert.All(Enum.GetValues<MaterialKind>(), k =>
            Assert.True(MaterialPreset.Delrin.SpecificEnergyJPerMm3 <= MaterialPreset.Get(k).SpecificEnergyJPerMm3));
    }
}
