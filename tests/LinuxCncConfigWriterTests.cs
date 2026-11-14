using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;

namespace CncTrainer.Tests;

public class LinuxCncConfigWriterTests
{
    private static AxisDef Linear(AxisId letter, float min, float max, float home = 0f, int sequence = 1) =>
        new(letter, false, min, max, 100f, 800f, home, sequence, Vector3.Zero);

    private static string Section(string ini, string name)
    {
        int start = ini.IndexOf($"[{name}]\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section {name}");
        int end = ini.IndexOf("\n[", start + 1, StringComparison.Ordinal);
        return end < 0 ? ini[start..] : ini[start..end];
    }

    [Fact]
    public void ThreeAxisMill_MatchesTheHandWrittenSimMillLimits()
    {
        string ini = LinuxCncConfigWriter.Ini("TEST", "gen_Test", new[]
        {
            Linear(AxisId.Z, -40f, 120f, home: 50f, sequence: 0),
            Linear(AxisId.X, -150f, 150f),
            Linear(AxisId.Y, -100f, 100f),
        }, null);

        Assert.Contains("COORDINATES = X Y Z\n", ini);
        Assert.Contains("KINEMATICS = trivkins coordinates=xyz\nJOINTS = 3\n", ini);
        Assert.Contains("PARAMETER_FILE = gen_Test.var\n", ini);
        Assert.Contains("TOOL_TABLE = gen_Test.tbl\n", ini);
        Assert.DoesNotContain("TOOL_CHANGE_POSITION", ini);
        Assert.DoesNotContain("ANGULAR_VELOCITY", ini);

        string z = Section(ini, "JOINT_2");
        Assert.Contains("TYPE = LINEAR\n", z);
        Assert.Contains("MIN_LIMIT = -40.000\n", z);
        Assert.Contains("MAX_LIMIT = 120.000\n", z);
        Assert.Contains("HOME = 50.000\n", z);
        Assert.Contains("HOME_SEQUENCE = 0", z);
        Assert.Contains("MAX_LIMIT = 150.000\n", Section(ini, "AXIS_X"));
        Assert.Contains("MIN_LIMIT = -100.000\n", Section(ini, "JOINT_1"));
    }

    [Fact]
    public void Lathe_ListsOnlyXAndZ_AndWritesTheToolChangePosition()
    {
        string ini = LinuxCncConfigWriter.Ini("TEST", "gen_Test",
            new[] { Linear(AxisId.X, -5f, 150f, sequence: 0), Linear(AxisId.Z, -300f, 50f) }, new Vector3(80f, 0f, 40f));

        Assert.Contains("COORDINATES = X Z\n", ini);
        Assert.Contains("KINEMATICS = trivkins coordinates=xz\nJOINTS = 2\n", ini);
        Assert.Contains("TOOL_CHANGE_POSITION = 80 0 40\n", ini);
        Assert.Contains("MIN_LIMIT = -300.000\n", Section(ini, "JOINT_1"));
    }

    [Fact]
    public void RotaryAxes_BecomeAngularJoints_InLetterOrder()
    {
        string ini = LinuxCncConfigWriter.Ini("TEST", "gen_Test", new[]
        {
            new AxisDef(AxisId.C, true, -360f, 360f, 90f, 600f, 0f, 2, Vector3.Zero),
            new AxisDef(AxisId.A, true, -110f, 110f, 60f, 400f, 0f, 2, new Vector3(0f, 0f, -50f)),
            Linear(AxisId.X, -200f, 200f), Linear(AxisId.Y, -150f, 150f), Linear(AxisId.Z, -100f, 200f),
        }, null);

        Assert.Contains("COORDINATES = X Y Z A C\n", ini);
        Assert.Contains("coordinates=xyzac\nJOINTS = 5\n", ini);
        Assert.Contains("MAX_ANGULAR_VELOCITY = 90.0\n", ini);
        Assert.Contains("TYPE = ANGULAR\n", Section(ini, "JOINT_3"));
        Assert.Contains("MAX_LIMIT = 110.000\n", Section(ini, "JOINT_3"));
        Assert.Contains("MAX_LIMIT = 360.000\n", Section(ini, "AXIS_C"));
    }

    [Fact]
    public void ToolTable_UsesInvariantNumbers_AndLatheFieldsOnlyForLatheTools()
    {
        string table = LinuxCncConfigWriter.ToolTable(new[]
        {
            new ToolDef(1, 10f, false, 0f, 0f, 0, "10mm end mill"),
            new ToolDef(2, 0.8f, true, 95f, 155f, 1, "roughing"),
        });

        Assert.Equal("T1 P1 D10.000000 Z+0.000000 ; 10mm end mill\n" +
                     "T2 P2 D0.800000 Z+0.000000 I+95.000000 J+155.000000 Q1 ; roughing\n", table);
    }
}
