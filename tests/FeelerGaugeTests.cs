using CncTrainer.Interaction;

namespace CncTrainer.Tests;

public class FeelerGaugeTests
{
    [Theory]
    [InlineData(49.98f, "girmiyor")]
    [InlineData(49.995f, "sıkı geçiyor")]
    [InlineData(50.008f, "sıkı geçiyor")]
    [InlineData(50.05f, "serbest")]
    public void SettingBlock_DragsOnlyWithinAHundredth(float gap, string expected)
    {
        var block = FeelerGauge.For(HandToolKind.GaugeBlock).Value;
        Assert.Contains(expected, block.Feel(gap));
    }

    [Fact]
    public void Paper_IsLooserThanTheBlock()
    {
        var paper = FeelerGauge.For(HandToolKind.PaperGauge).Value;
        Assert.Contains("sıkı geçiyor", paper.Feel(0.07f));
        Assert.Contains("YIRTILDI", paper.Feel(0.01f));
        Assert.Contains("uzak", paper.Feel(null));
        Assert.Null(FeelerGauge.For(HandToolKind.Caliper));
    }
}
