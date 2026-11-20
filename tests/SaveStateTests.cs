using CncTrainer.Fixtures;
using CncTrainer.Machining;
using CncTrainer.Material;
using Godot;

namespace Tests;

public class SaveStateTests
{
    [Fact]
    public void ChuckGrip_RestoresBarJawsAndRunout()
    {
        var grip = new ChuckGrip(80f, 30f, 30f, 250f);
        while (!grip.CanInsert(50f))
            grip.TurnKey(-1);
        grip.Insert(50f, 140f);
        grip.KeyIn = true;
        for (int i = 0; i < 40; i++)
            grip.TurnKey(1);
        grip.AddRunout(0.05f);

        var copy = new ChuckGrip(80f, 30f, 30f, 250f);
        copy.Restore(grip.Snapshot());

        Assert.True(copy.HasBar);
        Assert.Equal(grip.Torque, copy.Torque);
        Assert.True(copy.KeyIn);
        Assert.Equal(grip.OpeningMm, copy.OpeningMm);
        Assert.Equal(grip.StickOutMm, copy.StickOutMm);
        Assert.Equal(grip.RunoutMm(50f), copy.RunoutMm(50f));
        Assert.True(copy.HoldsFirmly);
    }

    [Fact]
    public void ChuckJaws_RestoresLooseAndOutOfTurnJaws()
    {
        var dims = new ChuckDims(ChuckKind.ThreeJaw, 3, 80f, 30f, 30f, 250f);
        var jaws = new ChuckJaws(dims, null);
        jaws.Place(1, JawKind.Hard, 1, ChuckKind.ThreeJaw, "");
        jaws.Advance(4);
        jaws.Place(2, JawKind.Hard, 3, ChuckKind.ThreeJaw, "");

        var copy = new ChuckJaws(dims, null);
        copy.Restore(jaws.Snapshot());

        Assert.Equal(jaws.Notch, copy.Notch);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(jaws.Slot(i), copy.Slot(i));
            Assert.Equal(jaws.Waiting(i), copy.Waiting(i));
        }
        Assert.Equal(jaws.MinRadiusMm, copy.MinRadiusMm);
    }

    [Fact]
    public void VoxelStock_RoundTripsItsDistanceField()
    {
        var stock = VoxelStock.Block(40f, 20f, 30f, 1f);
        stock.RemoveBeyond(new Vector3(10f, 0f, 0f), Vector3.Right, 0f);
        var copy = VoxelStock.FromDistances(stock.StepMm, stock.CountX, stock.CountY, stock.CountZ, stock.Origin, stock.Distances());

        Assert.NotNull(copy);
        Assert.Equal(stock.VolumeMm3(), copy!.VolumeMm3());
        Assert.Null(VoxelStock.FromDistances(1f, 2, 2, 2, Vector3.Zero, new float[3]));
    }

    [Fact]
    public void ToolState_RestoresWearAndBreakage()
    {
        var state = new ToolState();
        state.Restore(0.4f, false);
        Assert.Equal(0.4f, state.Wear);
        state.Restore(2f, true);
        Assert.Equal(1f, state.Wear);
        Assert.True(state.Broken);
    }
}
