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
/// <summary>Machine-wide INI settings beyond the axes. SpindleMaxRpm 0 =
/// no spindle speed limit written.</summary>
public readonly record struct IniExtras(float SpindleMaxRpm = 0f, float SpindleMinRpm = 0f, bool RandomToolChanger = false, string StartupCode = "",
    bool FanucToolCalls = false);

public static class LinuxCncConfigWriter
{
    /// <summary>T0101 calls, as LinuxCNC's lathe-fanucy sample does them:
    /// the T word goes to linuxcnc-config/fanuc_t.ngc (M6 + G43).</summary>
    private const string FanucRemap = "SUBROUTINE_PATH = ./\nREMAP = T prolog=get_T_number ngc=fanuc_t\n\n[PYTHON]\nPATH_PREPEND = ./\n"
        + "PATH_APPEND = /usr/share/linuxcnc/ncfiles/remap_lib/python-stdglue/\nTOPLEVEL = toplevel.py\n";

    private const float WrappedLimit = 99999f;

    public static string Ini(string machineName, string fileStem, IReadOnlyList<AxisDef> axes, Vector3? toolChangePosition, IniExtras extras = default)
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
{(string.IsNullOrWhiteSpace(extras.StartupCode) ? "" : "RS274NGC_STARTUP_CODE = " + extras.StartupCode.Trim() + "\n")}{(extras.FanucToolCalls ? FanucRemap : "")}
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
        if (extras.RandomToolChanger)
            sb.Append("RANDOM_TOOLCHANGER = 1\n");
        sb.Append(Invariant($@"TOOL_CHANGE_WITH_SPINDLE_ON = 0

[KINS]
KINEMATICS = trivkins coordinates={letters.ToLowerInvariant()}
JOINTS = {joints.Length}
"));

        if (extras.SpindleMaxRpm > 0f)
            sb.Append(Invariant($@"
[SPINDLE_0]
MAX_FORWARD_VELOCITY = {extras.SpindleMaxRpm:F0}
MIN_FORWARD_VELOCITY = {extras.SpindleMinRpm:F0}
"));

        for (int joint = 0; joint < joints.Length; joint++)
        {
            var a = joints[joint];
            float min = a.Wrapped ? -WrappedLimit : a.MinLimit + a.ControlOffset, max = a.Wrapped ? WrappedLimit : a.MaxLimit + a.ControlOffset;
            sb.Append(Invariant($@"
[AXIS_{a.Letter}]
MIN_LIMIT = {min:F3}
MAX_LIMIT = {max:F3}
MAX_VELOCITY = {a.MaxVelocity:F1}
MAX_ACCELERATION = {a.MaxAcceleration:F1}
{(a.Wrapped ? "WRAPPED_ROTARY = 1\n" : "")}
[JOINT_{joint}]
TYPE = {(a.IsRotary ? "ANGULAR" : "LINEAR")}
MAX_VELOCITY = {a.MaxVelocity:F1}
MAX_ACCELERATION = {a.MaxAcceleration:F1}
MIN_LIMIT = {min:F3}
MAX_LIMIT = {max:F3}
HOME = {a.Home + a.ControlOffset:F3}
HOME_SEARCH_VEL = {a.HomeSearchVel:F1}
HOME_LATCH_VEL = {a.HomeLatchVel:F1}
HOME_SEQUENCE = {a.HomeSequence}
{(a.Backlash > 0f ? Invariant($"BACKLASH = {a.Backlash:F4}\n") : "")}"));
        }
        return sb.ToString().Replace("\r\n", "\n");
    }

    public static string ToolTable(IEnumerable<ToolDef> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
        {
            sb.Append(Invariant($"T{t.Number} P{t.Number} D{t.TableDiameter:F6} Z{t.LengthZ:+0.000000;-0.000000}"));
            if (t.LatheTool)
                sb.Append(Invariant($" I{t.FrontAngle:+0.000000;-0.000000} J{t.BackAngle:+0.000000;-0.000000} Q{t.Orientation}"));
            sb.Append(" ; ").Append(t.Comment.Replace('\n', ' ')).Append('\n');
        }
        return sb.ToString();
    }
}
