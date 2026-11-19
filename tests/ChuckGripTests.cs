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

    private static ChuckGrip EmptyChuckWith(JawKind jaws)
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f) { KeyIn = true };
        Assert.Null(grip.ChangeJaws(jaws));
        grip.KeyIn = false;
        return grip;
    }

    private static bool Fits(ChuckGrip grip, float diameter)
    {
        for (int i = 0; i < 60 && !grip.CanInsert(diameter); i++)
            grip.TurnKey(-1);
        return grip.CanInsert(diameter);
    }

    [Fact]
    public void JawsChange_OnlyWithTheKeyIn_AndAnEmptyChuck()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        Assert.NotNull(grip.ChangeJaws(JawKind.Soft));
        Assert.Equal(JawKind.Hard, grip.Jaws);
        var loaded = LoadedChuck();
        loaded.KeyIn = true;
        Assert.NotNull(loaded.ChangeJaws(JawKind.Soft));
    }

    [Fact]
    public void ReversedJaws_TakeBigPartsButNotSmallOnes()
    {
        Assert.False(Fits(new ChuckGrip(80f, 30f, 30f, 250f), 95f));
        Assert.True(Fits(EmptyChuckWith(JawKind.HardReversed), 95f));
        Assert.False(Fits(EmptyChuckWith(JawKind.HardReversed), 20f));
        Assert.Contains("Ters", EmptyChuckWith(JawKind.HardReversed).WhyNot(20f));
    }

    [Fact]
    public void SoftJaws_HoldOnlyTheDiameterTheyWereBoredFor_AndRunNearlyTrue()
    {
        var grip = EmptyChuckWith(JawKind.Soft);
        Assert.False(Fits(grip, 50f));
        Assert.True(grip.BoreSoftJaws(50f));
        Assert.False(Fits(grip, 45f));
        Assert.True(Fits(grip, 50.2f));
        Assert.True(grip.Insert(50.2f, 140f));
        Assert.True(grip.RunoutMm(0f) < 0.016f, $"runout {grip.RunoutMm(0f)}");
    }

    [Fact]
    public void Bore_IsHeldFromInside_WhenTheJawsCloseSmallerThanIt()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        grip.KeyIn = true;
        Assert.False(grip.CanInsertInside(40f));
        while (grip.OpeningMm > 38f)
            grip.TurnKey(1);
        Assert.True(grip.InsertInside(40f, 25f, 70f, 60f));
        Assert.True(grip.Inside);
        Assert.Equal(40f, grip.GripDiameterMm);
        Assert.Equal(70f, grip.BarDiameterMm);
        Assert.Equal(35f, grip.StickOutMm);
        Assert.Equal(25f, grip.GripLengthMm);
    }

    [Fact]
    public void HeldFromInside_TheKeyOpensOutThenTightens()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        grip.KeyIn = true;
        while (grip.OpeningMm > 30f)
            grip.TurnKey(1);
        grip.InsertInside(40f, 25f, 70f, 60f);
        Assert.False(grip.AtContact);
        for (int i = 0; i < 10 && !grip.AtContact; i++)
            grip.TurnKey(1);
        Assert.True(grip.AtContact);
        Assert.Equal(0, grip.Torque);
        grip.TurnKey(1);
        grip.TurnKey(1);
        Assert.True(grip.HoldsFirmly);
        grip.TurnKey(-1);
        grip.TurnKey(-1);
        grip.TurnKey(-1);
        Assert.True(grip.OpeningMm < 40f);
    }

    [Fact]
    public void ShallowBore_DoesNotHold()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        grip.KeyIn = true;
        while (grip.OpeningMm > 30f)
            grip.TurnKey(1);
        grip.InsertInside(40f, 8f, 70f, 60f);
        for (int i = 0; i < 10; i++)
            grip.TurnKey(1);
        Assert.False(grip.HoldsFirmly);
    }

    [Fact]
    public void InsideGrip_NeedsHardJawsAndABoreTheyReach()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        Assert.NotNull(grip.WhyNotInside(10f));
        Assert.NotNull(grip.WhyNotInside(90f));
        Assert.Null(grip.WhyNotInside(40f));
        grip.KeyIn = true;
        grip.ChangeJaws(JawKind.Soft);
        Assert.NotNull(grip.WhyNotInside(40f));
    }

    private static ChuckGrip Fitted(ChuckKind kind)
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        Assert.Null(grip.ChangeChuck(kind));
        grip.KeyIn = true;
        return grip;
    }

    private static void Tighten(ChuckGrip grip, int torque)
    {
        for (int i = 0; i < 40 && grip.Torque < torque; i++)
            grip.TurnKey(1);
    }

    [Fact]
    public void Collet_TakesOnlySmallBars_AndHoldsThemNearlyTrue()
    {
        var grip = Fitted(ChuckKind.Collet);
        Assert.Equal(32f, grip.MaxGripDiameterMm);
        Assert.NotNull(grip.WhyNot(40f));
        Assert.NotNull(grip.WhyNotInside(20f));
        Assert.NotNull(grip.ChangeJaws(JawKind.Soft));
        while (!grip.CanInsert(20f)) grip.TurnKey(-1);
        Assert.True(grip.Insert(20f, 100f));
        Assert.True(grip.RunoutMm(0f) < 0.007f, $"runout {grip.RunoutMm(0f)}");
        Tighten(grip, 2);
        Assert.Contains("Pens", grip.Tap(0f));
    }

    [Fact]
    public void FourJaw_SeatsOffCentre_ButDialsInWhileTight()
    {
        var grip = Fitted(ChuckKind.FourJawIndependent);
        while (!grip.CanInsert(50f)) grip.TurnKey(-1);
        grip.Insert(50f, 120f);
        Tighten(grip, ChuckGrip.FullTorque);
        float before = grip.RunoutMm(0f);
        Assert.True(before > 0.15f, $"runout {before}");
        for (int i = 0; i < 6; i++)
        {
            var o = grip.OffsetMm;
            grip.Tap(System.MathF.Atan2(o.Y, o.X));
        }
        Assert.True(grip.RunoutMm(0f) < 0.01f, $"after {grip.RunoutMm(0f)}");
    }

    [Fact]
    public void ChuckChange_NeedsAnEmptyChuckAndTheKeyOut()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        grip.KeyIn = true;
        Assert.NotNull(grip.ChangeChuck(ChuckKind.Collet));
        grip.KeyIn = false;
        grip.Insert(20f, 100f);
        Assert.NotNull(grip.ChangeChuck(ChuckKind.Collet));
        grip.Remove();
        Assert.Null(grip.ChangeChuck(ChuckKind.Collet));
        Assert.Equal(ChuckKind.Collet, grip.Kind);
    }
}
