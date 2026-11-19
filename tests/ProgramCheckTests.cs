using CncTrainer.Bridge;
using CncTrainer.Machining;

namespace CncTrainer.Tests;

public class ProgramCheckTests
{
    /// <summary>A block standing at machine X 40..60 up to Z 10.</summary>
    private sealed class Block : IPathObstacle
    {
        public string CrashAt(AxisVector p) => p.X > 40f && p.X < 60f && p.Z < 10f ? "vise" : null;
    }

    private static PreviewSegment Move(int line, float x0, float z0, float x1, float z1, MotionType type = MotionType.Rapid) =>
        new() { LineNumber = line, MotionType = type, Start = new AxisVector(x0, 0f, z0), End = new AxisVector(x1, 0f, z1) };

    private static readonly IPathObstacle[] Setup = { new Block() };

    [Fact]
    public void RapidAcrossTheVise_IsFoundOnItsLine()
    {
        var path = new[] { Move(10, 0f, 50f, 0f, 5f), Move(20, 0f, 5f, 100f, 5f) };
        var hit = ProgramCheck.FirstConflict(path, AxisVector.Zero, new AxisVector(0f, 0f, 50f), Setup);
        Assert.NotNull(hit);
        Assert.Equal(20, hit.Value.LineNumber);
        Assert.Equal("vise", hit.Value.Reason);
        Assert.InRange(hit.Value.WorkPosition.X, 40f, 60f);
    }

    [Fact]
    public void PathAboveTheVise_IsClear()
    {
        var path = new[] { Move(10, 0f, 50f, 0f, 15f), Move(20, 0f, 15f, 100f, 15f) };
        Assert.Null(ProgramCheck.FirstConflict(path, AxisVector.Zero, new AxisVector(0f, 0f, 50f), Setup));
    }

    [Fact]
    public void WorkOffset_MovesThePathOntoTheMachine()
    {
        // Work Z 15 is machine Z 5 with the work zero 10 mm lower.
        var path = new[] { Move(20, 0f, 15f, 100f, 15f) };
        var hit = ProgramCheck.FirstConflict(path, new AxisVector(0f, 0f, -10f), new AxisVector(0f, 0f, 15f), Setup);
        Assert.Equal(20, hit?.LineNumber);
    }

    [Fact]
    public void FirstMove_StartsWhereTheMachineIs()
    {
        // The preview's first move starts at the program origin, inside the
        // block; the machine is really above it.
        var path = new[] { Move(10, 50f, 0f, 50f, 30f) };
        Assert.Null(ProgramCheck.FirstConflict(path, AxisVector.Zero, new AxisVector(50f, 0f, 40f), Setup));
    }

    [Fact]
    public void ShortObstacle_IsNotSteppedOver()
    {
        var thin = new[] { new ThinWall() };
        var path = new[] { Move(5, 0f, 0f, 1000f, 0f) };
        Assert.NotNull(ProgramCheck.FirstConflict(path, AxisVector.Zero, AxisVector.Zero, thin));
    }

    private sealed class ThinWall : IPathObstacle
    {
        public string CrashAt(AxisVector p) => p.X >= 500f && p.X <= 502.5f ? "wall" : null;
    }
}
