using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;

namespace CncTrainer.Tests;

public class ToolLengthModelTests
{
    private const float H = ToolLengthModel.HolderLengthMm;
    private static readonly ToolLengthModel Gauge = new(ToolLengthReference.SpindleGaugeLine, 80f, true);
    private static readonly ToolLengthModel Nominal = new(ToolLengthReference.NominalToolTip, 80f, true);

    /// <summary>Where the real tip ends up below the programmed Z: the
    /// control drives its Z (gauge line or nominal tip) to programmed Z plus
    /// the active table length, the reference point sits ZControlOffset
    /// below that, and the tip TipBelowReference below the reference point.</summary>
    private static float TipError(ToolLengthModel m, float toolMm, float errorMm, float activeTableMm) =>
        activeTableMm - m.ZControlOffsetMm - m.TipBelowReferenceMm(toolMm, errorMm);

    [Fact]
    public void WithG43AndAMeasuredTool_TheTipLandsOnTheProgrammedZ_InBothModels()
    {
        foreach (var m in new[] { Gauge, Nominal })
            Assert.Equal(0f, TipError(m, 50f, 1.7f, m.TableLengthMm(50f, 1.7f)), 3);
    }

    [Fact]
    public void ForgettingG43_DivesTheWholeGaugeLength_OnlyOnARealMachine()
    {
        Assert.Equal(-(50f + H + 1.7f), TipError(Gauge, 50f, 1.7f, 0f), 3);
        Assert.Equal(-1.7f, TipError(Nominal, 50f, 1.7f, 0f), 3);
    }

    [Fact]
    public void GaugeLine_KeepsTheSpindleStill_AndTheLongestToolsReach()
    {
        Assert.Equal(Gauge.NoseAboveReferenceMm(20f), Gauge.NoseAboveReferenceMm(80f));
        Assert.Equal(Nominal.NoseAboveReferenceMm(80f), Gauge.NoseAboveReferenceMm(80f));
        Assert.Equal(0f, Gauge.TipBelowReferenceMm(80f, 0f));
        Assert.Equal(-60f, Gauge.TipBelowReferenceMm(20f, 0f), 3);
    }

    [Fact]
    public void PresetLengths_DecideWhatAnUnmeasuredToolStartsWith()
    {
        Assert.Equal(50f + H, Gauge.InitialTableLengthMm(50f));
        Assert.Equal(0f, (Gauge with { PresetLengths = false }).InitialTableLengthMm(50f));
        Assert.Equal(0f, Nominal.InitialTableLengthMm(50f));
    }

    [Fact]
    public void ControlOffset_ShiftsTheIniAndIsTakenBackByTheKinematics()
    {
        var z = new AxisDef(AxisId.Z, false, -85f, 120f, 100f, 800f, 50f, 0, Vector3.Zero, ControlOffset: 140f);
        string ini = LinuxCncConfigWriter.Ini("M", "gen_M", new[] { z }, null);
        Assert.Contains("MIN_LIMIT = 55.000\n", ini);
        Assert.Contains("MAX_LIMIT = 260.000\n", ini);
        Assert.Contains("HOME = 190.000\n", ini);

        var kin = new MachineKinematics(new[] { z });
        Assert.Equal(0.010f, kin.ToolTransform(new AxisVector(z: 150f)).Origin.Y, 5);
    }

    [Fact]
    public void ToolTable_WritesTheLengthColumn()
    {
        string table = LinuxCncConfigWriter.ToolTable(new[] { new ToolDef(3, 10f, false, 0f, 0f, 0, "end mill", 110f) });
        Assert.Equal("T3 P3 D10.000000 Z+110.000000 ; end mill\n", table);
    }
}
