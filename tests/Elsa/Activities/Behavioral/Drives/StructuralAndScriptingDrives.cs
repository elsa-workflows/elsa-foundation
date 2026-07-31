using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Flowchart: Done when a path runs to the end, Break when any path reaches a Break (#304).</summary>
public sealed class FlowchartDrive : IActivityDrive
{
    public Type ActivityType => typeof(FlowchartActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, breakOut: false, ["actexec-flowchart", "actexec-a", "actexec-b"]);
        await RunAsync(recorder, breakOut: true, ["actexec-flowchart", "actexec-a"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, bool breakOut, string[] ids)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .Build(ids);

        var first = breakOut
            ? WorkflowExecutionHarness.NewProbeNode("node-a", ["Break"])
            : WorkflowExecutionHarness.NewProbeNode("node-a");
        var second = WorkflowExecutionHarness.NewProbeNode("node-b");
        var root = Nodes.Structural(
            "node-flowchart",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("node-a", null), new FlowchartEndpoint("node-b"))]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [first, second])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-flowchart");

        if (breakOut)
            run.AssertDidNotRun("node-b");
    }
}

/// <summary>
/// RunJavaScript: Done with no authored outcomes, and — with <c>PossibleOutcomes</c> authored — the port the
/// script's return value names plus the unmatched catch-all (#1114). The dynamic ports are per-node data the
/// publish compiler pins, so the drive supplies them on the node's contract exactly as the compiler would.
/// </summary>
public sealed class RunJavaScriptDrive : IActivityDrive
{
    public Type ActivityType => typeof(RunJavaScript);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // No authored outcomes: the implicit Done completion, with the result output populated.
        await RunAsync(recorder, "return { answer: 42 };", possibleOutcomes: null);

        // Authored outcomes: the script routes by returning a declared name, then an undeclared one.
        await RunAsync(recorder, "return 'approved';", ["approved", "rejected"], expectedOutcome: "approved");
        await RunAsync(recorder, "return 'nope';", ["approved", "rejected"], expectedOutcome: RunJavaScript.UnmatchedOutcome);
    }

    private async Task RunAsync(
        ActivityDriveRecorder recorder,
        string script,
        string[]? possibleOutcomes,
        string? expectedOutcome = null)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services))
            .WithFeature(services => Microsoft.Extensions.DependencyInjection.MemoryCacheServiceCollectionExtensions.AddMemoryCache(services))
            .Build("actexec-js");

        // 'arguments' is bound explicitly rather than left to the activity default: an unbound JsonElement input
        // resolves to default(JsonElement), which the runtime cannot clone into a value envelope.
        var inputs = new Dictionary<string, object?>
        {
            ["script"] = script,
            ["arguments"] = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        };
        if (possibleOutcomes is not null)
            inputs[RunJavaScript.PossibleOutcomesInputKey] = possibleOutcomes;

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Nodes.Leaf("node-js", typeof(RunJavaScript), inputs)));

        recorder.Record(ActivityType, run, "node-js");

        if (expectedOutcome is not null)
            recorder.RecordOutcome(ActivityType, expectedOutcome);
    }
}

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
