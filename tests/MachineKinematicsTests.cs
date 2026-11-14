using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;

namespace CncTrainer.Tests;

public class MachineKinematicsTests
{
    private static AxisDef Axis(AxisId letter, bool onWorkpiece, Vector3 pivotMm = default) =>
        new(letter, onWorkpiece, -1000f, 1000f, 100f, 800f, 0f, 0, pivotMm);

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void ToolInWork_IsTheSame_WhicheverSideCarriesALinearAxis()
    {
        var position = new AxisVector(10f, 20f, 30f);
        var expected = new Vector3(0.010f, 0.030f, -0.020f);

        var gantry = new MachineKinematics(new[] { Axis(AxisId.Y, false), Axis(AxisId.X, false), Axis(AxisId.Z, false) });
        var cFrame = new MachineKinematics(new[] { Axis(AxisId.Y, true), Axis(AxisId.X, true), Axis(AxisId.Z, false) });

        AssertNear(expected, gantry.ToolInWork(position).Origin);
        AssertNear(expected, cFrame.ToolInWork(position).Origin);
        AssertNear(new Vector3(-0.010f, 0f, 0.020f), cFrame.WorkTransform(position).Origin);
        AssertNear(new Vector3(0f, 0.030f, 0f), cFrame.ToolTransform(position).Origin);
    }

    [Fact]
    public void RotaryTable_SwingsTheToolAroundThePivot_SeenFromThePart()
    {
        var kin = new MachineKinematics(new[]
        {
            Axis(AxisId.X, false), Axis(AxisId.Y, false), Axis(AxisId.Z, false),
            Axis(AxisId.A, true, new Vector3(0f, 0f, -50f)),
        });

        var pose = kin.ToolInWork(new AxisVector(a: 90f));

        // The tip started 50mm above the pivot; +90 deg about X carries
        // "up" (machine +Z) onto machine -Y... seen in the local frame, +Z.
        var pivot = new Vector3(0f, -0.05f, 0f);
        AssertNear(pivot + new Vector3(0f, 0f, 0.05f), pose.Origin);
        AssertNear(new Vector3(0f, 0f, 1f), pose.Basis.Y);
    }

    [Fact]
    public void HeadRotary_TiltsTheTool_WithoutMovingTheWorkpiece()
    {
        var kin = new MachineKinematics(new[]
        {
            Axis(AxisId.X, false), Axis(AxisId.Z, false), Axis(AxisId.B, false, new Vector3(0f, 0f, 100f)),
        });

        var position = new AxisVector(b: 90f);
        AssertNear(Vector3.Zero, kin.WorkTransform(position).Origin);
        var pose = kin.ToolInWork(position);
        Assert.True(Mathf.Abs(pose.Basis.Y.Y) < 1e-4f);
        Assert.Equal(0.1f, pose.Origin.DistanceTo(new Vector3(0f, 0.1f, 0f)), 4);
    }
}
