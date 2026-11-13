using Godot;
using CncTrainer.Sim;

namespace CncTrainer.Tests;

public class PredictionModelTests
{
    [Fact]
    public void DeadReckons_FromLastConfirmedSample_WhenNoNewSampleArrives()
    {
        var model = new PredictionModel();
        model.OnConfirmedSample(new Vector2(10, 0), new Vector2(5, 0), timeSeconds: 0.0); // 5 mm/s along X

        // No new sample for 1 second: predicted position should extrapolate linearly.
        var predicted = model.PredictAt(1.0);

        Assert.Equal(15.0, predicted.X, precision: 3);
        Assert.Equal(0.0, predicted.Y, precision: 3);
    }

    [Fact]
    public void Update_SmoothsSmallErrors_InsteadOfSnapping()
    {
        var model = new PredictionModel { SnapThresholdMm = 10.0, SmoothingTimeConstantSeconds = 0.1 };
        model.OnConfirmedSample(new Vector2(0, 0), new Vector2(0, 0), 0.0);

        // A confirmed sample that disagrees with the dead-reckoned position by a
        // small amount (simulates noisy/late samples from a known path).
        model.OnConfirmedSample(new Vector2(2, 0), new Vector2(0, 0), 0.0);
        var visual = model.Update(nowSeconds: 0.0, frameDtSeconds: 1.0 / 60.0);

        // Should move PART of the way toward the 2mm error, not all of it, and
        // never past it either.
        Assert.True(visual.X > 0.0);
        Assert.True(visual.X < 2.0);
        Assert.False(model.LastCorrectionWasSnap);
    }

    [Fact]
    public void Update_SnapsInstantly_WhenErrorExceedsThreshold()
    {
        var model = new PredictionModel { SnapThresholdMm = 1.0 };
        model.OnConfirmedSample(new Vector2(0, 0), new Vector2(0, 0), 0.0);

        // A big, sudden jump (e.g. a dropped packet followed by a far-away
        // confirmed sample) should snap immediately rather than visibly glide.
        model.OnConfirmedSample(new Vector2(50, 0), new Vector2(0, 0), 0.0);
        var visual = model.Update(nowSeconds: 0.0, frameDtSeconds: 1.0 / 60.0);

        Assert.Equal(50.0, visual.X, precision: 3);
        Assert.True(model.LastCorrectionWasSnap);
    }

    [Fact]
    public void Update_ConvergesToConfirmedPosition_OverManyFrames_WithNoisyIntermediateSamples()
    {
        // "A known path with noisy samples" per the brief's testing requirement:
        // a straight-line move at 10 mm/s, with small per-sample position noise,
        // should still converge the visual position to the true final point.
        var model = new PredictionModel { SnapThresholdMm = 5.0, SmoothingTimeConstantSeconds = 0.05 };
        var rng = new Random(42);
        double t = 0.0;
        const double speed = 10.0; // mm/s along X
        const double dt = 1.0 / 100.0; // 100 Hz confirmed samples, matching the bridge's default rate

        model.OnConfirmedSample(new Vector2(0, 0), new Vector2((float)speed, 0), t);

        for (int i = 0; i < 300; i++) // 3 seconds of motion
        {
            t += dt;
            double truePos = speed * t;
            double noise = (rng.NextDouble() - 0.5) * 0.4; // +-0.2mm jitter
            model.OnConfirmedSample(new Vector2((float)(truePos + noise), 0), new Vector2((float)speed, 0), t);
            model.Update(t, dt);
        }

        double expectedFinal = speed * t;
        Assert.InRange(model.VisualPosition.X, expectedFinal - 2.0, expectedFinal + 2.0);
    }
}
