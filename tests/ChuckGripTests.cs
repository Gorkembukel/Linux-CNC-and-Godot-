using CncTrainer.Fixtures;
using CncTrainer.Material;

namespace Tests;

public class ChuckGripTests
{
    private static ChuckGrip LoadedChuck(float diameter = 50f, float length = 140f)
    {
        var grip = new ChuckGrip(maxOpeningMm: 80f, jawDepthMm: 30f, boreMm: 30f, boreDepthMm: 250f);
        while (!grip.CanInsert(diameter))
            grip.TurnKey(-1);
        Assert.True(grip.Insert(diameter, length));
        return grip;
    }

    [Fact]
    public void Insert_NeedsJawsOpenWiderThanTheBar()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        while (grip.OpeningMm > 40f)
            grip.TurnKey(1);
        Assert.False(grip.Insert(50f, 100f));
        Assert.False(grip.Insert(90f, 100f));
    }

    [Fact]
    public void Key_ClosesOntoTheBarThenTightens()
    {
        var grip = LoadedChuck();
        Assert.False(grip.HoldsFirmly);
        for (int i = 0; i < 40; i++)
            grip.TurnKey(1);
        Assert.Equal(50f, grip.OpeningMm);
        Assert.Equal(ChuckGrip.FullTorque, grip.Torque);
        Assert.True(grip.HoldsFirmly);
        grip.TurnKey(-1);
        grip.TurnKey(-1);
        Assert.False(grip.HoldsFirmly);
    }

    [Fact]
    public void ThickBar_StopsAtTheJawDepth_ThinBarGoesIntoTheBore()
    {
        var thick = LoadedChuck(50f, 140f);
        thick.Slide(-500f);
        Assert.Equal(110f, thick.StickOutMm);

        var thin = LoadedChuck(20f, 140f);
        thin.Slide(-500f);
        Assert.Equal(0f, thin.StickOutMm);
    }

    [Fact]
    public void BarPulledAlmostOut_DoesNotHoldEvenWhenTight()
    {
        var grip = LoadedChuck();
        grip.Slide(25f);
        for (int i = 0; i < 40; i++)
            grip.TurnKey(1);
        Assert.Equal(5f, grip.GripLengthMm);
        Assert.False(grip.HoldsFirmly);
    }

    [Fact]
    public void TightChuck_DoesNotLetTheBarSlide()
    {
        var grip = LoadedChuck();
        for (int i = 0; i < 40; i++)
            grip.TurnKey(1);
        grip.Slide(10f);
        Assert.Equal(110f, grip.StickOutMm);
    }

    [Fact]
    public void PartingOff_ShortensTheStickOut()
    {
        var grip = LoadedChuck();
        grip.SetLength(100f);
        Assert.Equal(70f, grip.StickOutMm);
        Assert.Equal(30f, grip.GripLengthMm);
    }

    [Fact]
    public void LiveRadii_EndAtTheFirstBucketCutThrough()
    {
        var profile = new LatheRadiusProfile(new[] { 25f, 25f, 20f, 0f, 25f }, zMin: -10f, stepMm: 0.5f);
        Assert.Equal(new[] { 25f, 25f, 20f }, profile.LiveRadii());
        Assert.Equal(-8f, profile.ZMax);
    }
}
