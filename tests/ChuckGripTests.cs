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

    private static readonly ChuckDims ThreeJaw = new(ChuckKind.ThreeJaw, 3, 80f, 30f, 30f, 250f);

    private static ChuckGrip EmptyChuckWith(JawKind jaws) => new(ThreeJaw, jaws);

    /// <summary>Opens the key until the next jaw comes off the scroll.</summary>
    private static SeatedJaw? NextJawOut(ChuckGrip grip)
    {
        for (int i = 0; i < 100; i++)
            if (grip.TurnKey(-1, canTakeJaw: true) is SeatedJaw jaw)
                return jaw;
        return null;
    }

    /// <summary>Turns the scroll on until slot (0-based) is the one to fill.</summary>
    private static void ScrollTo(ChuckGrip grip, int slot)
    {
        for (int i = 0; i < 30 && grip.Jaws.OpenSlot != slot; i++)
            grip.TurnKey(1);
    }

    private static bool Fits(ChuckGrip grip, float diameter)
    {
        for (int i = 0; i < 60 && !grip.CanInsert(diameter); i++)
            grip.TurnKey(-1);
        return grip.CanInsert(diameter);
    }

    [Fact]
    public void Jaws_ComeOffTheScrollOneByOne_LastInFirstOut()
    {
        var grip = new ChuckGrip(ThreeJaw) { KeyIn = true };
        Assert.Equal(3, NextJawOut(grip)?.Number);
        Assert.Equal(2, NextJawOut(grip)?.Number);
        Assert.Equal(1, NextJawOut(grip)?.Number);
        Assert.True(grip.Jaws.Empty);
        Assert.NotNull(grip.WhyNot(40f));
    }

    [Fact]
    public void AJawWaitingInHand_StopsTheKey()
    {
        var grip = new ChuckGrip(ThreeJaw) { KeyIn = true };
        for (int i = 0; i < 100; i++)
            Assert.Null(grip.TurnKey(-1, canTakeJaw: false));
        Assert.True(grip.Jaws.Complete);
    }

    [Fact]
    public void JawsInByNumber_AsTheScrollReachesEachSlot_MeetAtTheCentre()
    {
        var grip = new ChuckGrip(ThreeJaw, fitted: null) { KeyIn = true };
        for (int n = 1; n <= 3; n++)
        {
            ScrollTo(grip, n - 1);
            Assert.Null(grip.InstallJaw(JawKind.HardReversed, n, ChuckKind.ThreeJaw));
        }
        Assert.True(grip.Jaws.Complete);
        Assert.True(grip.Jaws.MeetAtCentre);
        Assert.Equal(JawKind.HardReversed, grip.JawSet);
    }

    [Fact]
    public void AJawCaughtATurnLate_HoldsThePartOff()
    {
        var grip = new ChuckGrip(ThreeJaw, fitted: null) { KeyIn = true };
        foreach (int n in new[] { 2, 3, 1 })
        {
            ScrollTo(grip, n - 1);
            Assert.Null(grip.InstallJaw(JawKind.Hard, n, ChuckKind.ThreeJaw));
        }
        Assert.False(grip.Jaws.MeetAtCentre);
        for (int i = 0; i < 30; i++) grip.TurnKey(-1, canTakeJaw: false);
        Assert.True(grip.Insert(40f, 100f));
        Assert.True(grip.RunoutMm(0f) > 1f, $"runout {grip.RunoutMm(0f)}");
    }

    [Fact]
    public void JawInTheWrongSlot_HoldsThePartOff()
    {
        var grip = new ChuckGrip(ThreeJaw, fitted: null) { KeyIn = true };
        foreach (var (number, slot) in new[] { (2, 0), (1, 1), (3, 2) })
        {
            ScrollTo(grip, slot);
            Assert.Null(grip.InstallJaw(JawKind.Hard, number, ChuckKind.ThreeJaw));
        }
        Assert.False(grip.Jaws.MeetAtCentre);
    }

    [Fact]
    public void Jaws_GoInOnlyWithTheKey_AnEmptyChuck_AndFromOneSet()
    {
        var grip = new ChuckGrip(ThreeJaw, fitted: null);
        Assert.NotNull(grip.InstallJaw(JawKind.Hard, 1, ChuckKind.ThreeJaw));
        grip.KeyIn = true;
        Assert.NotNull(grip.InstallJaw(JawKind.Hard, 1, ChuckKind.FourJawIndependent));
        Assert.Null(grip.InstallJaw(JawKind.Hard, 1, ChuckKind.ThreeJaw));
        ScrollTo(grip, 1);
        Assert.NotNull(grip.InstallJaw(JawKind.Soft, 2, ChuckKind.ThreeJaw));
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
        Assert.NotNull(EmptyChuckWith(JawKind.Soft).WhyNotInside(40f));
    }

    private static void Tighten(ChuckGrip grip, int torque)
    {
        for (int i = 0; i < 40 && grip.Torque < torque; i++)
            grip.TurnKey(1);
    }

    private static readonly ChuckDims Er32 = new(ChuckKind.Collet, 0, 20f, 30f, 20f, 250f);

    [Fact]
    public void Collet_HoldsOnlyItsOwnSize_AndLetsGoWhenLoosened()
    {
        var grip = new ChuckGrip(Er32) { KeyIn = true };
        Assert.Contains("pens yok", grip.WhyNot(20f));
        Assert.Null(grip.InstallCollet(20f));
        Assert.NotNull(grip.WhyNot(21f));
        Assert.NotNull(grip.WhyNot(18f));
        Assert.NotNull(grip.WhyNotInside(20f));
        Assert.True(grip.Insert(19.5f, 100f));
        Assert.True(grip.RunoutMm(0f) < 0.007f, $"runout {grip.RunoutMm(0f)}");
        Tighten(grip, ChuckGrip.FullTorque);
        Assert.True(grip.HoldsFirmly);
        Assert.Contains("Pens", grip.Tap(0f));
        float stick = grip.StickOutMm;
        grip.Slide(-10f);
        Assert.Equal(stick, grip.StickOutMm);
        for (int i = 0; i < 3; i++) grip.TurnKey(-1);
        Assert.Equal(0, grip.Torque);
        grip.Slide(-10f);
        Assert.Equal(stick - 10f, grip.StickOutMm);
        Assert.Null(grip.TakeCollet());
        grip.Remove();
        Assert.Equal(20f, grip.TakeCollet());
    }

    [Fact]
    public void FourJaw_SeatsOffCentre_ButDialsInWhileTight()
    {
        var grip = new ChuckGrip(new ChuckDims(ChuckKind.FourJawIndependent, 4, 110f, 30f, 32f, 250f)) { KeyIn = true };
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
    public void FourJaw_JawsGoInAnyOrder()
    {
        var grip = new ChuckGrip(new ChuckDims(ChuckKind.FourJawIndependent, 4, 110f, 30f, 32f, 250f), fitted: null) { KeyIn = true };
        foreach (int n in new[] { 3, 1, 4, 2 })
            Assert.Null(grip.InstallJaw(JawKind.Hard, n, ChuckKind.FourJawIndependent));
        Assert.True(grip.Jaws.Complete);
    }

    [Fact]
    public void BenchJaw_WaitsInItsGroove_UntilTheScrollStartComesRound()
    {
        var jaws = new ChuckJaws(ThreeJaw, fitted: null);
        Assert.Null(jaws.Place(1, JawKind.Hard, 2, ChuckKind.ThreeJaw, ""));
        Assert.Equal(0, jaws.Seated);
        Assert.Equal(2, jaws.Waiting(1)?.Number);
        for (int i = 0; i < ChuckJaws.NotchesPerSlot; i++)
            jaws.Advance(1);
        Assert.Equal(1, jaws.Seated);
        Assert.Null(jaws.Waiting(1));
    }

    [Fact]
    public void BenchJaws_LaidInAndCaughtInOrder_MeetAtTheCentre()
    {
        var jaws = new ChuckJaws(ThreeJaw, fitted: null);
        Assert.Null(jaws.Place(0, JawKind.Hard, 1, ChuckKind.ThreeJaw, ""));
        Assert.Null(jaws.Place(1, JawKind.Hard, 2, ChuckKind.ThreeJaw, ""));
        Assert.Null(jaws.Place(2, JawKind.Hard, 3, ChuckKind.ThreeJaw, ""));
        for (int i = 0; i < 9 && !jaws.Complete; i++)
            jaws.Advance(1);
        Assert.True(jaws.Complete);
        Assert.True(jaws.MeetAtCentre);
    }

    [Fact]
    public void JawsFitOnlyTheirOwnChuckSize()
    {
        var dims = ThreeJaw with { JawFamily = "ThreeJaw-125" };
        var jaws = new ChuckJaws(dims, fitted: null);
        Assert.NotNull(jaws.WhyNotJaw(0, JawKind.Hard, 1, ChuckKind.ThreeJaw, "ThreeJaw-160"));
        Assert.Null(jaws.WhyNotJaw(0, JawKind.Hard, 1, ChuckKind.ThreeJaw, "ThreeJaw-125"));
    }
}
