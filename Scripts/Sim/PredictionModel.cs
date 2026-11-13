using System;
using System.Collections.Generic;
using Godot;

namespace CncTrainer.Sim;

/// <summary>
/// Client-side prediction/reconciliation for the tool's render-time position,
/// analogous to prediction in networked games (brief section 3). Pure logic, no
/// Godot engine dependency beyond the Vector2 struct, so it's unit-testable with
/// plain `dotnet test` (see Tests/).
///
/// Approach: dead-reckon from the last CONFIRMED sample's position + velocity.
/// This handles both JOG/MDI (no known path -- exactly the brief's fallback) and
/// AUTO/running-a-program cases, since the bridge's reported velocity is the
/// real instantaneous velocity either way. What this does NOT do: thread the
/// prediction along the specific G-code segment geometry during a corner (e.g.
/// an arc), which would need the preview path's segment list cross-referenced
/// against motion_line -- scoped out for now; flagged in PLAN.md as a
/// deliberate simplification, not an oversight. Small errors between the
/// predicted and actual position are corrected with exponential smoothing
/// (equivalent to a critically damped 1st-order filter); errors beyond
/// SnapThresholdMm snap instantly instead of smoothing through a visible glide.
/// </summary>
public class PredictionModel
{
    public double SmoothingTimeConstantSeconds = 0.08;
    public double SnapThresholdMm = 3.0;

    public const int ErrorHistoryCapacity = 240; // ~4s at 60 fps

    private Vector2 _confirmedPos;
    private Vector2 _confirmedVel;
    private double _confirmedTimeSeconds;
    private bool _hasConfirmed;

    public Vector2 VisualPosition { get; private set; }
    public Vector2 LastPredictedPosition { get; private set; }
    public double LastErrorMagnitudeMm { get; private set; }
    public bool LastCorrectionWasSnap { get; private set; }

    private readonly Queue<double> _errorHistory = new();

    public IReadOnlyCollection<double> ErrorHistory => _errorHistory;

    public void OnConfirmedSample(Vector2 pos, Vector2 vel, double timeSeconds)
    {
        _confirmedPos = pos;
        _confirmedVel = vel;
        _confirmedTimeSeconds = timeSeconds;
        if (!_hasConfirmed)
        {
            VisualPosition = pos;
            LastPredictedPosition = pos;
            _hasConfirmed = true;
        }
    }

    public Vector2 PredictAt(double nowSeconds)
    {
        if (!_hasConfirmed)
            return Vector2.Zero;
        float dt = (float)(nowSeconds - _confirmedTimeSeconds);
        return _confirmedPos + _confirmedVel * dt;
    }

    /// <summary>Call once per rendered frame. Returns the position to draw the tool at.</summary>
    public Vector2 Update(double nowSeconds, double frameDtSeconds)
    {
        if (!_hasConfirmed)
            return Vector2.Zero;

        Vector2 predicted = PredictAt(nowSeconds);
        LastPredictedPosition = predicted;

        Vector2 error = predicted - VisualPosition;
        double errorMag = error.Length();
        LastErrorMagnitudeMm = errorMag;
        RecordError(errorMag);

        if (errorMag > SnapThresholdMm)
        {
            VisualPosition = predicted;
            LastCorrectionWasSnap = true;
        }
        else
        {
            double alpha = frameDtSeconds >= SmoothingTimeConstantSeconds
                ? 1.0
                : 1.0 - Math.Exp(-frameDtSeconds / SmoothingTimeConstantSeconds);
            VisualPosition += error * (float)alpha;
            LastCorrectionWasSnap = false;
        }
        return VisualPosition;
    }

    private void RecordError(double errorMag)
    {
        _errorHistory.Enqueue(errorMag);
        while (_errorHistory.Count > ErrorHistoryCapacity)
            _errorHistory.Dequeue();
    }
}
