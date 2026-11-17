using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using static System.FormattableString;

namespace CncTrainer.Spec;

/// <summary>
/// Builds the LinuxCNC sim INI and tool table for a machine from its axis and
/// tool lists. Same layout as the hand-written linuxcnc-config/sim_mill.ini.
/// </summary>
public static class LinuxCncConfigWriter
{
    public static string Ini(string machineName, string fileStem, IReadOnlyList<AxisDef> axes, Vector3? toolChangePosition)
    {
        var joints = AxisDef.InJointOrder(axes);
        string letters = string.Concat(joints.Select(a => a.Letter.ToString()));
        var linear = joints.Where(a => !a.IsRotary).ToArray();
        var rotary = joints.Where(a => a.IsRotary).ToArray();
        float maxLinearVelocity = linear.Max(a => a.MaxVelocity);
        float maxLinearAcceleration = linear.Max(a => a.MaxAcceleration);

        var sb = new StringBuilder();
        sb.Append(Invariant($@"[EMC]
VERSION = 1.1
MACHINE = {machineName}
DEBUG = 0

[DISPLAY]
DISPLAY = dummy
CYCLE_TIME = 0.050
POSITION_OFFSET = RELATIVE
POSITION_FEEDBACK = ACTUAL
MAX_FEED_OVERRIDE = 1.5
MAX_SPINDLE_OVERRIDE = 1.2
MIN_SPINDLE_OVERRIDE = 0.5
PROGRAM_PREFIX = ./nc_files
INTRO_GRAPHIC =
INTRO_TIME = 0

[FILTER]
PROGRAM_EXTENSION = .py Python Script
py = python3

[RS274NGC]
PARAMETER_FILE = {fileStem}.var

[EMCMOT]
EMCMOT = motmod
COMM_TIMEOUT = 1.0
BASE_PERIOD = 50000
SERVO_PERIOD = 1000000

[TASK]
TASK = milltask
CYCLE_TIME = 0.001

[HAL]
HALUI = halui
HALFILE = LIB:basic_sim.tcl -no_use_hal_manualtoolchange

[TRAJ]
COORDINATES = {string.Join(" ", letters.ToCharArray())}
LINEAR_UNITS = mm
ANGULAR_UNITS = degree
DEFAULT_LINEAR_VELOCITY = {Mathf.Min(20f, maxLinearVelocity):F1}
MAX_LINEAR_VELOCITY = {maxLinearVelocity:F1}
DEFAULT_LINEAR_ACCELERATION = {maxLinearAcceleration / 2f:F1}
MAX_LINEAR_ACCELERATION = {maxLinearAcceleration:F1}
"));
        if (rotary.Length > 0)
        {
            float maxAngularVelocity = rotary.Max(a => a.MaxVelocity);
            sb.Append(Invariant($"DEFAULT_ANGULAR_VELOCITY = {Mathf.Min(20f, maxAngularVelocity):F1}\n"));
            sb.Append(Invariant($"MAX_ANGULAR_VELOCITY = {maxAngularVelocity:F1}\n"));
        }

        sb.Append(Invariant($@"
[EMCIO]
EMCIO = io
CYCLE_TIME = 0.100
TOOL_TABLE = {fileStem}.tbl
"));
        if (toolChangePosition is Vector3 p)
            sb.Append(Invariant($"TOOL_CHANGE_POSITION = {p.X:0.###} {p.Y:0.###} {p.Z:0.###}\n"));
        sb.Append(Invariant($@"TOOL_CHANGE_WITH_SPINDLE_ON = 0

[KINS]
KINEMATICS = trivkins coordinates={letters.ToLowerInvariant()}
JOINTS = {joints.Length}
"));

        for (int joint = 0; joint < joints.Length; joint++)
        {
            var a = joints[joint];
            sb.Append(Invariant($@"
[AXIS_{a.Letter}]
MIN_LIMIT = {a.MinLimit:F3}
MAX_LIMIT = {a.MaxLimit:F3}
MAX_VELOCITY = {a.MaxVelocity:F1}
MAX_ACCELERATION = {a.MaxAcceleration:F1}

[JOINT_{joint}]
TYPE = {(a.IsRotary ? "ANGULAR" : "LINEAR")}
MAX_VELOCITY = {a.MaxVelocity:F1}
MAX_ACCELERATION = {a.MaxAcceleration:F1}
MIN_LIMIT = {a.MinLimit:F3}
MAX_LIMIT = {a.MaxLimit:F3}
HOME = {a.Home:F3}
HOME_SEARCH_VEL = 20.0
HOME_LATCH_VEL = 5.0
HOME_SEQUENCE = {a.HomeSequence}
"));
        }
        return sb.ToString().Replace("\r\n", "\n");
    }

    public static string ToolTable(IEnumerable<ToolDef> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
        {
            sb.Append(Invariant($"T{t.Number} P{t.Number} D{t.TableDiameter:F6} Z+0.000000"));
            if (t.LatheTool)
                sb.Append(Invariant($" I{t.FrontAngle:+0.000000;-0.000000} J{t.BackAngle:+0.000000;-0.000000} Q{t.Orientation}"));
            sb.Append(" ; ").Append(t.Comment.Replace('\n', ' ')).Append('\n');
        }
        return sb.ToString();
    }
}
