using CncTrainer.Chips;
using CncTrainer.Machining;

namespace Tests;

public class ChipEvacuationTests
{
    private static ChipEvacuation.Verdict Run(ChipEvacuation chips, float seconds, float depthDiameters, MaterialPreset material, bool coolant)
    {
        var verdict = ChipEvacuation.Verdict.Clear;
        for (float t = 0f; t < seconds && verdict != ChipEvacuation.Verdict.Packed; t += 0.05f)
            verdict = chips.Track(0.05f, 300f, depthDiameters, material, coolant);
        return verdict;
    }

    [Fact]
    public void DeepDryPocketInAluminium_PacksTheFlutes()
    {
        Assert.Equal(ChipEvacuation.Verdict.Packed, Run(new ChipEvacuation(), 10f, 1.5f, MaterialPreset.Aluminum, coolant: false));
    }

    [Fact]
    public void CoolantShallowCutsOrBrittleChips_StayClear()
    {
        Assert.Equal(ChipEvacuation.Verdict.Clear, Run(new ChipEvacuation(), 20f, 1.5f, MaterialPreset.Aluminum, coolant: true));
        Assert.Equal(ChipEvacuation.Verdict.Clear, Run(new ChipEvacuation(), 20f, 0.3f, MaterialPreset.Aluminum, coolant: false));
        Assert.Equal(ChipEvacuation.Verdict.Clear, Run(new ChipEvacuation(), 20f, 1.5f, MaterialPreset.Brass, coolant: false));
    }

    [Fact]
    public void StoppingTheCut_LetsChipsFallOut()
    {
        var chips = new ChipEvacuation();
        Run(chips, 3f, 1.5f, MaterialPreset.Aluminum, coolant: false);
        float trapped = chips.TrappedMm3;
        chips.Clear(1f, coolant: true);
        Assert.True(chips.TrappedMm3 < trapped);
    }
}
