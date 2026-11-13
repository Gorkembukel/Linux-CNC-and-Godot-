using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CncTrainer.Scheduling;

public enum JobPriority { Critical, High, Normal, Low }

/// <summary>A unit of deferrable, resumable work. Step() does as much as it can
/// within the given budget and returns true when fully finished, false if it
/// needs to be called again (continuing where it left off) on a later frame --
/// brief section 4's "long jobs must be resumable... continue next frame from
/// where they stopped."</summary>
public interface IJob
{
    bool Step(double budgetMs);
}

/// <summary>Wraps a plain synchronous action that always completes in one Step
/// call -- most of this project's jobs (a single mesh chunk rebuild, a chip
/// tick) are already short enough not to need true multi-frame slicing, so
/// this covers them without extra ceremony.</summary>
public sealed class AtomicJob : IJob
{
    private readonly Action _action;
    public AtomicJob(Action action) => _action = action;
    public bool Step(double budgetMs) { _action(); return true; }
}

/// <summary>
/// Brief section 4: a scheduler with priority classes, time-slicing, and an
/// adaptive budget for the low/normal classes based on recent frame times.
/// Framework-agnostic (no Godot dependency) so it's unit-testable; Main.cs owns
/// one instance and feeds it real jobs every frame.
///
/// Scope note: this coordinates CPU time budget on the main thread (time-
/// slicing jobs across frames so none of them spikes frame time), it does not
/// itself dispatch work onto Godot's WorkerThreadPool -- true background-
/// thread mesh generation was evaluated and deliberately deferred; see
/// PLAN.md's Phase 5 status for the reasoning (this project's actual
/// measured per-frame costs, see PERF.md, did not justify the added
/// complexity and thread-safety surface of moving mesh generation off-thread).
/// </summary>
public class JobScheduler
{
    private static readonly JobPriority[] PriorityOrder =
        { JobPriority.Critical, JobPriority.High, JobPriority.Normal, JobPriority.Low };

    public readonly Dictionary<JobPriority, double> BudgetMs = new()
    {
        [JobPriority.Critical] = double.PositiveInfinity, // never throttled -- brief: "must finish every frame"
        [JobPriority.High] = 4.0,
        [JobPriority.Normal] = 2.0,
        [JobPriority.Low] = 1.0,
    };

    // Low-priority jobs older than this are considered stale and dropped
    // outright rather than run late ("coalesced or dropped" per the brief).
    public double LowPriorityMaxAgeMs = 500.0;

    public readonly Dictionary<JobPriority, double> LastUsedMs = new();
    public readonly Dictionary<JobPriority, int> QueueLengths = new();
    public readonly Dictionary<JobPriority, int> DroppedCount = new();

    private readonly Dictionary<JobPriority, Queue<(IJob job, double enqueuedAtMs)>> _queues = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public JobScheduler()
    {
        foreach (var p in PriorityOrder)
        {
            _queues[p] = new();
            LastUsedMs[p] = 0;
            QueueLengths[p] = 0;
            DroppedCount[p] = 0;
        }
    }

    public void Enqueue(JobPriority priority, IJob job)
    {
        _queues[priority].Enqueue((job, _clock.Elapsed.TotalMilliseconds));
    }

    public void Enqueue(JobPriority priority, Action action) => Enqueue(priority, new AtomicJob(action));

    /// <summary>Run one frame's worth of work. Call once per _Process.</summary>
    public void RunFrame()
    {
        foreach (var priority in PriorityOrder)
        {
            var queue = _queues[priority];
            double budget = BudgetMs[priority];
            double start = _clock.Elapsed.TotalMilliseconds;
            double nowMs = start;

            // Drop stale low-priority work instead of ever running it late.
            if (priority == JobPriority.Low)
            {
                while (queue.Count > 0 && nowMs - queue.Peek().enqueuedAtMs > LowPriorityMaxAgeMs)
                {
                    queue.Dequeue();
                    DroppedCount[priority]++;
                }
            }

            while (queue.Count > 0)
            {
                double elapsed = nowMs - start;
                if (priority != JobPriority.Critical && elapsed >= budget)
                    break; // resume the rest next frame

                var (job, _) = queue.Peek();
                double remaining = priority == JobPriority.Critical ? double.PositiveInfinity : budget - elapsed;
                bool done = job.Step(remaining);
                if (done)
                    queue.Dequeue();
                nowMs = _clock.Elapsed.TotalMilliseconds;
                if (!done)
                    break; // this job wants to continue later -- don't starve other priorities behind it
            }

            LastUsedMs[priority] = nowMs - start;
            QueueLengths[priority] = queue.Count;
        }
    }

    /// <summary>Brief section 4: "adjust the budgets of the low and normal
    /// classes dynamically based on the last N frame times." Shrinks Normal/Low
    /// budgets under sustained load, grows them back toward their defaults
    /// (captured once, at construction) when there's headroom.</summary>
    public void AdaptBudgets(double recentAvgFrameMs, double targetFrameMs)
    {
        double pressure = recentAvgFrameMs / targetFrameMs; // >1 = running over budget
        float scale = pressure > 1.0
            ? Math.Max(0.2f, 1.0f / (float)pressure)
            : Math.Min(1.0f, (float)(1.0 + (targetFrameMs - recentAvgFrameMs) / targetFrameMs * 0.1));

        BudgetMs[JobPriority.Normal] = _defaultNormalBudget * scale;
        BudgetMs[JobPriority.Low] = _defaultLowBudget * scale;
    }

    private readonly double _defaultNormalBudget = 2.0;
    private readonly double _defaultLowBudget = 1.0;
}
