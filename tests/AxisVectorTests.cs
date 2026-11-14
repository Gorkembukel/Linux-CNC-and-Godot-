using CncTrainer.Bridge;

namespace CncTrainer.Tests;

public class AxisVectorTests
{
    [Fact]
    public void Get_ReturnsTheNamedField_ForEachAxis()
    {
        var v = new AxisVector(1, 2, 3, 4, 5, 6);
        Assert.Equal(1f, v.Get(AxisId.X), precision: 3);
        Assert.Equal(2f, v.Get(AxisId.Y), precision: 3);
        Assert.Equal(3f, v.Get(AxisId.Z), precision: 3);
        Assert.Equal(4f, v.Get(AxisId.A), precision: 3);
        Assert.Equal(5f, v.Get(AxisId.B), precision: 3);
        Assert.Equal(6f, v.Get(AxisId.C), precision: 3);
    }

    [Fact]
    public void Arithmetic_OperatesComponentwise()
    {
        var a = new AxisVector(1, 2, 3);
        var b = new AxisVector(10, 20, 30);

        var sum = a + b;
        Assert.Equal(11f, sum.X, precision: 3);
        Assert.Equal(22f, sum.Y, precision: 3);
        Assert.Equal(33f, sum.Z, precision: 3);

        var diff = b - a;
        Assert.Equal(9f, diff.X, precision: 3);

        var scaled = a * 2f;
        Assert.Equal(2f, scaled.X, precision: 3);
        Assert.Equal(6f, scaled.Z, precision: 3);
    }

    [Fact]
    public void Length_IsTheEuclideanNormAcrossAllSixAxes()
    {
        var v = new AxisVector(3, 4); // 3-4-5 triangle on just X/Y
        Assert.Equal(5f, v.Length(), precision: 3);
    }
}
