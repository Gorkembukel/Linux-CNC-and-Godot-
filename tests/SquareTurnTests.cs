using Godot;
using CncTrainer.Interaction;

namespace CncTrainer.Tests;

public class SquareTurnTests
{
    [Fact]
    public void Snap_RoundsATiltedBasisToTheNearestQuarterTurns()
    {
        var tilted = new Basis(Vector3.Up, Mathf.DegToRad(80f)) * new Basis(Vector3.Right, Mathf.DegToRad(10f));
        var snapped = SquareTurn.Snap(tilted);

        Assert.Equal(Vector3.Forward, snapped.X);
        Assert.Equal(Vector3.Up, snapped.Y);
        Assert.Equal(1f, snapped.Determinant(), precision: 4);
    }

    [Fact]
    public void Extents_SwapWithTheTurn()
    {
        var size = new Vector3(100f, 30f, 60f);
        var standingUp = SquareTurn.QuarterTurn(Basis.Identity, Vector3.Back);

        Assert.Equal(new Vector3(30f, 100f, 60f), SquareTurn.Extents(standingUp, size));
        Assert.Equal(size, SquareTurn.Extents(Basis.Identity, size));
    }

    [Fact]
    public void FourQuarterTurns_ComeBackToTheStart()
    {
        var turn = Basis.Identity;
        for (int i = 0; i < 4; i++)
            turn = SquareTurn.QuarterTurn(turn, Vector3.Up);
        Assert.True(turn.IsEqualApprox(Basis.Identity));
    }
}
