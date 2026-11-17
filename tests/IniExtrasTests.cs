using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;

namespace CncTrainer.Tests;

public class IniExtrasTests
{
    private static string Section(string ini, string name)
    {
        int start = ini.IndexOf($"[{name}]\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section {name}");
        int end = ini.IndexOf("\n[", start + 1, StringComparison.Ordinal);
        return end < 0 ? ini[start..] : ini[start..end];
    }

    [Fact]
    public void Extras_WriteSpindleLimits_StartupCode_AndRandomChanger()
    {
        var axes = new[]
        {
            new AxisDef(AxisId.X, false, -5f, 150f, 100f, 800f, 0f, 0, Vector3.Zero),
            new AxisDef(AxisId.Z, false, -300f, 50f, 100f, 800f, 0f, 1, Vector3.Zero),
        };
        string ini = LinuxCncConfigWriter.Ini("L", "gen_L", axes, null, new IniExtras(3000f, 50f, true, "G8 G18"));

        Assert.Contains("RS274NGC_STARTUP_CODE = G8 G18\n", Section(ini, "RS274NGC"));
        Assert.Contains("RANDOM_TOOLCHANGER = 1\n", Section(ini, "EMCIO"));
        Assert.Contains("MAX_FORWARD_VELOCITY = 3000\n", Section(ini, "SPINDLE_0"));
        Assert.Contains("MIN_FORWARD_VELOCITY = 50\n", Section(ini, "SPINDLE_0"));
    }

    [Fact]
    public void NoExtras_LeaveTheOldIniAlone()
    {
        var axes = new[] { new AxisDef(AxisId.X, false, -5f, 150f, 100f, 800f, 0f, 0, Vector3.Zero) };
        string ini = LinuxCncConfigWriter.Ini("L", "gen_L", axes, null);

        Assert.DoesNotContain("SPINDLE_0", ini);
        Assert.DoesNotContain("STARTUP_CODE", ini);
        Assert.DoesNotContain("RANDOM_TOOLCHANGER", ini);
        Assert.DoesNotContain("BACKLASH", ini);
        Assert.Contains("HOME_SEARCH_VEL = 20.0\n", Section(ini, "JOINT_0"));
    }

    [Fact]
    public void Axis_WritesHomingDirection_Backlash_AndWrappedRotary()
    {
        var axes = new[]
        {
            new AxisDef(AxisId.X, false, -100f, 100f, 100f, 800f, 0f, 0, Vector3.Zero, HomeSearchVel: -15f, HomeLatchVel: 2f, Backlash: 0.02f),
            new AxisDef(AxisId.C, true, 0f, 360f, 90f, 800f, 0f, 1, Vector3.Zero, Wrapped: true),
        };
        string ini = LinuxCncConfigWriter.Ini("M", "gen_M", axes, null);

        string x = Section(ini, "JOINT_0");
        Assert.Contains("HOME_SEARCH_VEL = -15.0\n", x);
        Assert.Contains("HOME_LATCH_VEL = 2.0\n", x);
        Assert.Contains("BACKLASH = 0.0200\n", x);
        Assert.Contains("WRAPPED_ROTARY = 1\n", Section(ini, "AXIS_C"));
        Assert.Contains("MAX_LIMIT = 99999.000\n", Section(ini, "JOINT_1"));
    }
}
