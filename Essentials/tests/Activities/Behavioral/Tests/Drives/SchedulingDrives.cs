using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// Timer and Cron are trigger activities that, executed mid-flow, echo their schedule as their result. Both are
/// driven here; the recurring firing itself is a scheduler concern covered by the scheduling suite.
/// </summary>
public sealed class TimerDrive : IActivityDrive
{
    public Type ActivityType => typeof(TimerActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = SchedulingHarness.New("actexec-timer");
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Nodes.Leaf("node-timer", typeof(TimerActivity), new Dictionary<string, object?> { ["Interval"] = TimeSpan.FromMinutes(5) })));

        recorder.Record(ActivityType, run, "node-timer");
        run.AssertWorkflowCompleted();
    }
}

/// <inheritdoc cref="TimerDrive"/>
public sealed class CronDrive : IActivityDrive
{
    public Type ActivityType => typeof(Cron);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = SchedulingHarness.New("actexec-cron");
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Nodes.Leaf("node-cron", typeof(Cron), new Dictionary<string, object?> { ["Expression"] = "0 0 * * *" })));

        recorder.Record(ActivityType, run, "node-cron");
        run.AssertWorkflowCompleted();
    }
}

/// <summary>
/// Delay suspends on a durable timer and completes only when that timer is delivered, so the drive does the full
/// round trip: run → suspend → resume. Completing it is the only way to show the Done outcome is reachable.
/// </summary>
public sealed class DelayDrive : IActivityDrive
{
    public Type ActivityType => typeof(Delay);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        const string nodeId = "node-delay";
        const string resumeTargetId = "resume-target:durable-timer";
        await using var harness = SchedulingHarness.New("actexec-delay");

        var leaf = Nodes.Leaf(nodeId, typeof(Delay), new Dictionary<string, object?> { ["Duration"] = TimeSpan.FromSeconds(5) });
        var scopedResumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, resumeTargetId);
        var executable = new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            leaf,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [scopedResumeTargetId] = new(scopedResumeTargetId, nodeId, "ResumeAsync", new Dictionary<string, string>(), resumeTargetId)
            },
            new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);

        var suspended = await harness.RunAsync(executable);
        var waiting = suspended.State(nodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);

        var timerId = $"timer:{waiting.InvocationId}";
        var resumed = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: nodeId,
            resumeTargetId: scopedResumeTargetId,
            stimulusType: "DurableTimer",
            stimulusHash: timerId,
            input: System.Text.Json.JsonSerializer.SerializeToElement(new DurableTimerElapsed(timerId)));

        recorder.Record(ActivityType, resumed, nodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, resumed.State(nodeId).Status);
    }
}

/// <summary>Shared harness wiring for the scheduling activities.</summary>
internal static class SchedulingHarness
{
    public static WorkflowExecutionHarness New(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .Build(activityExecutionIds);
}
