using CncTrainer.Scheduling;

namespace CncTrainer.Tests;

/// <summary>A job that takes a fixed number of Step() calls to finish,
/// regardless of budget -- simulates genuinely long, resumable work.</summary>
public class CountingStepsJob : IJob
{
    public int RemainingSteps;
    public int TimesStepped;
    public CountingStepsJob(int steps) => RemainingSteps = steps;
    public bool Step(double budgetMs)
    {
        TimesStepped++;
        RemainingSteps--;
        return RemainingSteps <= 0;
    }
}

public class JobSchedulerTests
{
    [Fact]
    public void CriticalJobs_AlwaysRun_RegardlessOfBudget()
    {
        var scheduler = new JobScheduler();
        scheduler.BudgetMs[JobPriority.Critical] = 0.0; // even an explicit zero budget
        bool ran = false;
        scheduler.Enqueue(JobPriority.Critical, () => ran = true);

        scheduler.RunFrame();

        Assert.True(ran);
    }

    [Fact]
    public void ResumableJob_ContinuesAcrossMultipleRunFrameCalls()
    {
        var scheduler = new JobScheduler();
        var job = new CountingStepsJob(steps: 3);
        scheduler.Enqueue(JobPriority.High, job);

        scheduler.RunFrame();
        Assert.Equal(2, job.RemainingSteps);
        Assert.Equal(1, scheduler.QueueLengths[JobPriority.High]); // still queued, not finished

        scheduler.RunFrame();
        scheduler.RunFrame();
        Assert.Equal(0, job.RemainingSteps);
        Assert.Equal(0, scheduler.QueueLengths[JobPriority.High]); // dequeued once done
        Assert.Equal(3, job.TimesStepped);
    }

    [Fact]
    public void HigherPriorityJobs_RunBeforeLowerPriorityOnes_WithinTheSameFrame()
    {
        var scheduler = new JobScheduler();
        var order = new System.Collections.Generic.List<string>();
        scheduler.Enqueue(JobPriority.Low, () => order.Add("low"));
        scheduler.Enqueue(JobPriority.Normal, () => order.Add("normal"));
        scheduler.Enqueue(JobPriority.High, () => order.Add("high"));
        scheduler.Enqueue(JobPriority.Critical, () => order.Add("critical"));

        scheduler.RunFrame();

        Assert.Equal(new[] { "critical", "high", "normal", "low" }, order);
    }

    [Fact]
    public void StaleLowPriorityJobs_AreDroppedRatherThanRunLate()
    {
        var scheduler = new JobScheduler { LowPriorityMaxAgeMs = 0.0 }; // anything queued is instantly "stale"
        bool ran = false;
        scheduler.Enqueue(JobPriority.Low, () => ran = true);

        scheduler.RunFrame();

        Assert.False(ran);
        Assert.Equal(1, scheduler.DroppedCount[JobPriority.Low]);
        Assert.Equal(0, scheduler.QueueLengths[JobPriority.Low]);
    }

    [Fact]
    public void AdaptBudgets_ShrinksNormalAndLowBudgets_UnderSustainedFrameTimePressure()
    {
        var scheduler = new JobScheduler();
        double defaultNormal = scheduler.BudgetMs[JobPriority.Normal];
        double defaultLow = scheduler.BudgetMs[JobPriority.Low];

        scheduler.AdaptBudgets(recentAvgFrameMs: 66.0, targetFrameMs: 33.3); // running at ~15fps, want 30fps

        Assert.True(scheduler.BudgetMs[JobPriority.Normal] < defaultNormal);
        Assert.True(scheduler.BudgetMs[JobPriority.Low] < defaultLow);
    }

    [Fact]
    public void AdaptBudgets_NeverExpandsPastDefaults_WhenFrameTimeIsWellUnderTarget()
    {
        var scheduler = new JobScheduler();
        double defaultNormal = scheduler.BudgetMs[JobPriority.Normal];

        scheduler.AdaptBudgets(recentAvgFrameMs: 5.0, targetFrameMs: 33.3); // way under budget

        Assert.True(scheduler.BudgetMs[JobPriority.Normal] <= defaultNormal * 1.01);
    }
}
