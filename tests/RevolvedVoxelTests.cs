using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class RevolvedVoxelTests
{
    [Fact]
    public void Revolved_FollowsTheTurnedProfile_AndItsBore()
    {
        // 40 mm long: Ø40 for the first half, Ø20 for the second, Ø10 bore through.
        var radii = new float[81];
        var inner = new float[81];
        for (int i = 0; i <= 80; i++)
        {
            radii[i] = i <= 40 ? 20f : 10f;
            inner[i] = 5f;
        }
        var stock = VoxelStock.Revolved(radii, inner, 0.5f, 0.5f);

        // Axis at y = -20 (top line of the largest diameter at 0).
        Assert.True(stock.SampleDistance(new Vector3(-10f, -20f + 15f, 0f)) > 0f);   // in the Ø40 wall
        Assert.True(stock.SampleDistance(new Vector3(10f, -20f + 15f, 0f)) < 0f);    // above the Ø20 step
        Assert.True(stock.SampleDistance(new Vector3(10f, -20f + 8f, 0f)) > 0f);     // in the Ø20 wall
        Assert.True(stock.SampleDistance(new Vector3(0f, -20f, 0f)) < 0f);           // in the bore
        Assert.True(stock.SampleDistance(new Vector3(-25f, -20f + 10f, 0f)) < 0f);   // beyond the end
    }
}
