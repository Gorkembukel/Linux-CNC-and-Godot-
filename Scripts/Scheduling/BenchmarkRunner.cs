using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Material;
using CncTrainer.Chips;

namespace CncTrainer;

/// <summary>
/// Brief section 4: "Build a benchmark scene with heavy cutting, a long
/// program, and many chips, and write the results to PERF.md." Runs entirely
/// synthetically (no LinuxCNC/bridge needed) so it's reproducible: sweeps the
/// material model continuously across the whole stock every frame (heavy
/// cutting) and forces a sustained high-rate "continuous" chip formation
/// (many chips), for a fixed number of frames, then prints frame-time
/// percentiles and scheduler stats and quits.
/// </summary>
public class BenchmarkRunner
{
    private readonly Node _owner;
    private readonly LatheRadiusProfile _material;
    private readonly WorkpieceVisual _workpiece;
    private readonly ChipSpawner _chips;
    private readonly Scheduling.JobScheduler _scheduler;

    private readonly List<double> _frameTimesMs = new();
    private int _frameCount;
    private const int TotalFrames = 1800; // ~30s at 60fps / ~60s at 30fps
    private float _sweepZ;
    private readonly ChipFormation _heavyFormation;

    public BenchmarkRunner(Node owner, LatheRadiusProfile material, WorkpieceVisual workpiece,
        ChipSpawner chips, Scheduling.JobScheduler scheduler)
    {
        _owner = owner;
        _material = material;
        _workpiece = workpiece;
        _chips = chips;
        _scheduler = scheduler;
        _sweepZ = material.ZMax;
        _heavyFormation = ChipClassifier.Classify(MaterialPreset.Steel, depthOfCutMm: 1.0f, feedPerRevMm: 0.3f, cuttingSpeedMmPerSec: 2500f);
        GD.Print($"[Benchmark] starting: {TotalFrames} frames of synthetic heavy cutting + continuous chip spawning");
    }

    public void Tick(double delta)
    {
        _frameTimesMs.Add(delta * 1000.0);
        _frameCount++;

        // Heavy cutting: sweep a constantly-advancing, constantly-shrinking
        // radius the length of the whole stock, wrapping around -- guarantees
        // every frame does real mesh-dirtying work, not just idle polling.
        float fromZ = _sweepZ;
        _sweepZ -= 2.0f; // mm per frame -- fast enough to keep re-touching the whole chunk range
        if (_sweepZ < _material.ZMin)
            _sweepZ = _material.ZMax;
        float radius = 10f + 10f * Mathf.Sin((float)Engine.GetFramesDrawn() * 0.01f); // oscillate 0-20mm depth
        var result = _material.Sweep(new Vector2(radius, fromZ), new Vector2(radius, _sweepZ), 0.4f, 0.2f, 1000f);
        if (result.MaterialRemoved)
            _workpiece.MarkDirty(result.MinDirtyIndex, result.MaxDirtyIndex);

        // Many chips: force a sustained heavy formation every frame.
        var contactPoint = new Vector3(_sweepZ, radius, 0f) * WorldScale.MmToM;
        _chips.Tick(delta, _heavyFormation, contactPoint);

        if (_frameCount >= TotalFrames)
            Finish();
    }

    private void Finish()
    {
        var sorted = _frameTimesMs.OrderBy(x => x).ToList();
        double Pct(double p) => sorted[Mathf.Clamp((int)(p * sorted.Count), 0, sorted.Count - 1)];

        GD.Print("[Benchmark] RESULTS");
        GD.Print($"[Benchmark] frames={_frameCount}");
        GD.Print($"[Benchmark] avg={_frameTimesMs.Average():F3} ms  min={sorted.First():F3} ms  max={sorted.Last():F3} ms");
        GD.Print($"[Benchmark] p50={Pct(0.50):F3} ms  p95={Pct(0.95):F3} ms  p99={Pct(0.99):F3} ms");
        GD.Print($"[Benchmark] implied avg fps={1000.0 / _frameTimesMs.Average():F1}  implied p95 fps={1000.0 / Pct(0.95):F1}");
        GD.Print($"[Benchmark] scheduler: High used~{_scheduler.LastUsedMs[Scheduling.JobPriority.High]:F2}ms " +
                  $"Normal used~{_scheduler.LastUsedMs[Scheduling.JobPriority.Normal]:F2}ms " +
                  $"Low dropped={_scheduler.DroppedCount[Scheduling.JobPriority.Low]}");
        GD.Print("[Benchmark] DONE");

        _owner.GetTree().Quit();
    }
}
