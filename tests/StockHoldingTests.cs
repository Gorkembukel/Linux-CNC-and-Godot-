using CncTrainer.Fixtures;

namespace Tests;

public class StockHoldingTests
{
    [Fact]
    public void Runout_IsTheEdgeLengthTimesTheTangentOfTheAngle()
    {
        Assert.Equal(1.126f, Tram.Runout(0.43f, 150f), 3);
        Assert.Equal(0f, Tram.Runout(0f, 150f), 5);
    }

    [Fact]
    public void CoarseThenFineTaps_BringAViseWithinTolerance()
    {
        float yaw = 0.43f;
        int taps = 0;
        while (yaw > 0.05f && taps++ < 20)
            yaw = Tram.Tapped(yaw, -1f, fine: false);
        Assert.Equal(0.03f, yaw, 3);
        while (System.Math.Abs(Tram.Runout(yaw, 150f)) > Tram.AlignedWithinMm && taps++ < 40)
            yaw = Tram.Tapped(yaw, -1f, fine: true);
        Assert.True(System.Math.Abs(Tram.Runout(yaw, 150f)) <= Tram.AlignedWithinMm);
        Assert.True(taps < 10);
    }

    [Fact]
    public void Tap_OnTheOtherEnd_TurnsTheOtherWay()
    {
        Assert.Equal(0.1f, Tram.Tapped(0f, 1f, fine: false), 3);
        Assert.Equal(-0.01f, Tram.Tapped(0f, -1f, fine: true), 3);
    }
}
