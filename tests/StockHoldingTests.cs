using CncTrainer.Fixtures;

namespace Tests;

public class StockHoldingTests
{
    [Fact]
    public void Runout_IsTheEdgeLengthTimesTheTangentOfTheAngle()
    {
        Assert.Equal(1.126f, StockHolding.RunoutMm(0.43f, 150f), 3);
        Assert.Equal(0f, StockHolding.RunoutMm(0f, 150f), 5);
    }

    [Fact]
    public void CoarseThenFineTaps_BringAViseWithinTolerance()
    {
        float yaw = 0.43f;
        int taps = 0;
        while (yaw > 0.05f && taps++ < 20)
            yaw = StockHolding.Tap(yaw, -1f, fine: false);
        Assert.Equal(0.03f, yaw, 3);
        while (System.Math.Abs(StockHolding.RunoutMm(yaw, 150f)) > StockHolding.AlignedWithinMm && taps++ < 40)
            yaw = StockHolding.Tap(yaw, -1f, fine: true);
        Assert.True(System.Math.Abs(StockHolding.RunoutMm(yaw, 150f)) <= StockHolding.AlignedWithinMm);
        Assert.True(taps < 10);
    }

    [Fact]
    public void Tap_OnTheOtherEnd_TurnsTheOtherWay()
    {
        Assert.Equal(0.1f, StockHolding.Tap(0f, 1f, fine: false), 3);
        Assert.Equal(-0.01f, StockHolding.Tap(0f, -1f, fine: true), 3);
    }
}
