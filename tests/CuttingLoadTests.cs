using CncTrainer.Chips;
using CncTrainer.Machining;

namespace Tests;

public class CuttingLoadTests
{
    private static readonly ToolLimits EndMill10 = new(Lathe: false, Parting: false, DiameterMm: 10f, StickOutMm: 50f, Flutes: 2);
    private static readonly ToolLimits EndMill6 = new(false, false, 6f, 50f, 2);
    private static readonly ToolLimits TurningTool = new(true, false, 0f, 50f, 1);

    // Peripheral speed of a cutter (mm/s).
    private static float Speed(float diameterMm, float rpm) => MathF.PI * diameterMm * rpm / 60f;

    [Fact]
    public void Power_IsRemovalRateTimesSpecificEnergy()
    {
        var load = CuttingLoad.Evaluate(150f, 3000f, Speed(10f, 3000f), 0.1f, EndMill10, MaterialPreset.Aluminum, 1500f);
        Assert.Equal(105f, load.PowerW, 1);
        Assert.Equal(0.07f, load.SpindleLoad, 3);
        Assert.Equal(CutFault.None, load.Fault);
    }

    [Fact]
    public void SameCut_LoadsTheSpindleMoreInSteelThanInAluminium()
    {
        var aluminium = CuttingLoad.Evaluate(300f, 3000f, Speed(10f, 3000f), 0.1f, EndMill10, MaterialPreset.Aluminum, 1500f);
        var steel = CuttingLoad.Evaluate(300f, 3000f, Speed(10f, 3000f), 0.1f, EndMill10, MaterialPreset.Steel, 1500f);
        Assert.True(steel.SpindleLoad > 3f * aluminium.SpindleLoad);
    }

    [Fact]
    public void DeepFastSlot_SnapsASmallEndMill_ButNotALargeOne()
    {
        // 6 mm wide, 10 mm deep, 25 mm/s in aluminium.
        var small = CuttingLoad.Evaluate(1500f, 3000f, Speed(6f, 3000f), 0.5f, EndMill6, MaterialPreset.Aluminum, 11000f);
        Assert.Equal(CutFault.ToolOverload, small.Fault);
        // The same removal rate with a 10 mm cutter.
        var large = CuttingLoad.Evaluate(1500f, 3000f, Speed(10f, 3000f), 0.5f, EndMill10, MaterialPreset.Aluminum, 11000f);
        Assert.Equal(CutFault.None, large.Fault);
    }

    [Fact]
    public void HeavyCut_StallsASmallSpindle()
    {
        // 2 mm deep, 0.19 mm/rev on a 50 mm steel bar at 800 rpm.
        float speed = Speed(50f, 800f);
        float removalRate = 2f * 0.19f * speed;
        Assert.Equal(CutFault.None, CuttingLoad.Evaluate(removalRate, 800f, speed, 0.19f, TurningTool, MaterialPreset.Steel, 3000f).Fault);
        Assert.Equal(CutFault.SpindleOverload, CuttingLoad.Evaluate(removalRate, 800f, speed, 0.19f, TurningTool, MaterialPreset.Steel, 1000f).Fault);
    }

    [Fact]
    public void CuttingWithTheSpindleStopped_IsAFault()
    {
        Assert.Equal(CutFault.SpindleStopped, CuttingLoad.Evaluate(50f, 0f, 0f, 0f, EndMill10, MaterialPreset.Aluminum, 1500f).Fault);
    }

    [Fact]
    public void DoublingTheCuttingSpeed_WearsTheToolSixteenTimesFaster()
    {
        float recommended = MaterialPreset.Steel.RecommendedSpeedMPerMin / 0.06f;
        var normal = CuttingLoad.Evaluate(100f, 800f, recommended, 0.15f, TurningTool, MaterialPreset.Steel, 5000f);
        var fast = CuttingLoad.Evaluate(100f, 1600f, recommended * 2f, 0.15f, TurningTool, MaterialPreset.Steel, 5000f);
        Assert.Equal(1f / CuttingLoad.ToolLifeSeconds, normal.WearPerSecond, 6);
        Assert.Equal(16f, fast.WearPerSecond / normal.WearPerSecond, 1);
    }

    [Fact]
    public void TooFineAFeed_RubsAndWearsFaster()
    {
        float recommended = MaterialPreset.Steel.RecommendedSpeedMPerMin / 0.06f;
        var sound = CuttingLoad.Evaluate(100f, 800f, recommended, 0.15f, TurningTool, MaterialPreset.Steel, 5000f);
        var rubbing = CuttingLoad.Evaluate(100f, 800f, recommended, 0.02f, TurningTool, MaterialPreset.Steel, 5000f);
        Assert.False(sound.Rubbing);
        Assert.True(rubbing.Rubbing);
        Assert.True(rubbing.WearPerSecond > sound.WearPerSecond);
    }

    [Fact]
    public void FacingToTheCentre_DoesNotBlowUpTheForce()
    {
        var load = CuttingLoad.Evaluate(0.5f, 800f, 0.8f, 0.2f, TurningTool, MaterialPreset.Steel, 3000f);
        Assert.Equal(CutFault.None, load.Fault);
    }

    [Fact]
    public void WornOutTool_Breaks_AndAFreshOneReplacesIt()
    {
        var crib = new ToolCrib();
        Assert.False(crib[1].AddWear(0.6f));
        Assert.Equal(0.09f, crib[1].EdgeLossMm, 3);
        Assert.True(crib[1].AddWear(0.6f));
        Assert.True(crib.IsBroken(1));
        crib.Replace(1);
        Assert.False(crib.IsBroken(1));
        Assert.Equal(0f, crib[1].Wear);
    }

    [Fact]
    public void ACutterSlidOutFurther_ChattersSooner()
    {
        var cut = (rate: 700f, rpm: 3000f);
        var short10 = CuttingLoad.Evaluate(cut.rate, cut.rpm, Speed(10f, cut.rpm), 0.1f, EndMill10 with { StickOutMm = 30f }, MaterialPreset.Steel, 5000f);
        var long10 = CuttingLoad.Evaluate(cut.rate, cut.rpm, Speed(10f, cut.rpm), 0.1f, EndMill10 with { StickOutMm = 70f }, MaterialPreset.Steel, 5000f);
        Assert.False(short10.Chatter);
        Assert.True(long10.Chatter);
        Assert.True(long10.ToolLoad > short10.ToolLoad);
    }

    [Fact]
    public void ABarStickingFarOutOfTheChuck_ChattersUnderALightCut()
    {
        var rigid = CuttingLoad.Evaluate(800f, 800f, 1500f, 0.15f, TurningTool, MaterialPreset.Steel, 5000f);
        var slender = CuttingLoad.Evaluate(800f, 800f, 1500f, 0.15f, TurningTool, MaterialPreset.Steel, 5000f, workSlenderness: 6f);
        Assert.False(rigid.Chatter);
        Assert.True(slender.Chatter);
        Assert.False(CuttingLoad.Evaluate(800f, 800f, 1500f, 0.15f, TurningTool, MaterialPreset.Steel, 5000f, workSlenderness: 3f).Chatter);
    }
}
