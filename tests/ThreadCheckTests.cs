using Godot;
using CncTrainer.Material;

namespace CncTrainer.Tests;

public class ThreadCheckTests
{
    /// <summary>A thread cut the way G76 does it, on a 0.1 mm profile.</summary>
    private static LatheRadiusProfile Threaded(float radius, float pitch, float depth)
    {
        var bar = new LatheRadiusProfile(radius, -60f, 0f, 0.1f);
        var tool = new ToolProfile(InsertOutline.Build(new InsertParams(InsertShape.Thread60, 0f, 0.1f, 90f, ToolHand.Neutral, DepthMm: 4f)));
        for (float d = 0.2f; d < depth + 0.15f; d += 0.2f)
            bar.SweepProfile(new Vector2(radius - Mathf.Min(d, depth), 2f), new Vector2(radius - Mathf.Min(d, depth), -30f), tool, threadPitchMm: pitch, anchorZ: 2f);
        return bar;
    }

    [Fact]
    public void Measure_ReadsPitchAndDiameters()
    {
        var bar = Threaded(10f, 2f, 1.23f);
        var form = ThreadCheck.Measure(bar.LiveRadii(), bar.StepMm, bar.IndexAt(-15f)).Value;

        Assert.Equal(2f, form.PitchMm, precision: 2);
        Assert.InRange(form.MajorMm, 19.9f, 20.01f);
        Assert.InRange(form.MinorMm, 17.4f, 17.7f);
        // A sharp 60 deg insert at the nominal depth leaves the grooves narrow: a fat thread.
        Assert.InRange(form.PitchDiameterMm, 19.0f, 19.3f);
        Assert.NotNull(ThreadCheck.GoRefusal(form, ThreadSpec.M20x2));
        Assert.Null(ThreadCheck.Measure(bar.LiveRadii(), bar.StepMm, bar.IndexAt(-50f)));
    }

    [Fact]
    public void TurnedUnderAndCutDeeper_TheM20x2PassesTheGoAndStopsTheNoGo()
    {
        var bar = Threaded(9.925f, 2f, 1.41f);
        var form = ThreadCheck.Measure(bar.LiveRadii(), bar.StepMm, bar.IndexAt(-15f)).Value;

        Assert.Null(ThreadCheck.GoRefusal(form, ThreadSpec.M20x2));
        Assert.False(ThreadCheck.NoGoEnters(form, ThreadSpec.M20x2));
    }

    [Fact]
    public void Length_RunsToWhereTheThreadStops()
    {
        var bar = Threaded(10f, 2f, 1.23f);
        int end = bar.LiveRadii().Length - 1;
        float mm = ThreadCheck.Length(bar.LiveRadii(), bar.StepMm, end, -1, 2f) * bar.StepMm;
        Assert.InRange(mm, 28f, 33f);
    }

    [Fact]
    public void Gauges_GoOnInTolerance_RefuseThickOrWrongPitch_NoGoCatchesTooDeep()
    {
        var spec = ThreadSpec.M20x2;
        Assert.Null(ThreadCheck.GoRefusal(new ThreadForm(2f, 19.9f, 17.5f, 18.6f), spec));
        Assert.False(ThreadCheck.NoGoEnters(new ThreadForm(2f, 19.9f, 17.5f, 18.6f), spec));
        Assert.NotNull(ThreadCheck.GoRefusal(new ThreadForm(2f, 19.9f, 17.6f, 18.75f), spec));
        Assert.NotNull(ThreadCheck.GoRefusal(new ThreadForm(1.5f, 19.9f, 17.6f, 18.6f), spec));
        Assert.True(ThreadCheck.NoGoEnters(new ThreadForm(2f, 19.9f, 17.2f, 18.4f), spec));
    }
}
