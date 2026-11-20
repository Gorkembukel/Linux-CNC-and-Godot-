using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Bridge;

namespace CncTrainer.Spec;

/// <summary>
/// Engine-free copy of one axis of a MachineSpec (a Godot Resource cannot be
/// constructed outside the engine, so the config writer and the kinematics
/// take these instead and stay unit-testable). Linear axes are in mm, rotary
/// (A/B/C) in degrees. PivotMm is a point on a rotary axis' centreline, in
/// machine coordinates relative to work zero with every axis at 0.
/// ControlOffset: the control's value minus the physical one (limits, home
/// and pivot here are physical).
/// </summary>
public readonly record struct AxisDef(
    AxisId Letter, bool OnWorkpiece, float MinLimit, float MaxLimit,
    float MaxVelocity, float MaxAcceleration, float Home, int HomeSequence, Vector3 PivotMm,
    float HomeSearchVel = 20f, float HomeLatchVel = 5f, float Backlash = 0f, bool Wrapped = false, float ControlOffset = 0f)
{
    public bool IsRotary => Letter >= AxisId.A;

    /// <summary>trivkins numbers joints in the order the letters are listed,
    /// and the generated INI lists them in X Y Z A B C order.</summary>
    public static AxisDef[] InJointOrder(IEnumerable<AxisDef> axes) => axes.OrderBy(a => a.Letter).ToArray();
}

public readonly record struct ToolDef(
    int Number, float TableDiameter, bool LatheTool, float FrontAngle, float BackAngle, int Orientation, string Comment, float LengthZ = 0f, float OffsetX = 0f);
