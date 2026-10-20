using System.Globalization;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

/// <summary>
/// Spec 260 (ADR 0088 D2), T014: a condition-driven loop re-materializes its inputs on every pass, so a <c>Clock</c>
/// parameter in its condition reads that pass's own instant and a <c>UniqueId</c> in its body differs per pass. The
/// host clock is a <see cref="ManualClock"/> the loop body advances by one second each pass; a condition pinned to
/// the entry instant would never become false and the drain would hit its cycle limit.
/// </summary>
public sealed class WhileClockParameterTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 8, 30, 15, 123, TimeSpan.Zero);
    private static readonly string[] ActivityExecutionIds =
        ["actexec-loop", "actexec-body-0", "actexec-body-1", "actexec-body-2", "actexec-body-3", "actexec-body-4"];

    [Fact]
    public async Task A_while_condition_reads_each_passes_own_instant_and_exits_once_the_deadline_passes()
    {
        var clock = new ManualClock(Start);
        await using var harness = NewHarness(clock, new PassPlan());

        // Entry at Start, then re-checks at Start+1s and Start+2s hold; the re-check at Start+3s is past the deadline.
        var run = await harness.RunAsync(NewExecutable(While(Start.AddSeconds(2.5))));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-loop", ActivityOutcomes.Done);
        var passes = run.States("node-body").Select(Pass).ToArray();
        Assert.Equal([Iso(Start), Iso(Start.AddSeconds(1)), Iso(Start.AddSeconds(2))], passes.Select(pass => pass.Now));
        // Each body pass's Clock is that pass's materialization instant.
        Assert.All(run.States("node-body"), state => Assert.Equal(Iso(state.InputSnapshot!.MaterializedAt), Pass(state).Now));
    }

    [Fact]
    public async Task A_unique_id_in_the_body_differs_per_pass()
    {
        var clock = new ManualClock(Start);
        await using var harness = NewHarness(clock, new PassPlan());

        var run = await harness.RunAsync(NewExecutable(While(Start.AddSeconds(2.5))));

        run.AssertWorkflowCompleted();
        var ids = run.States("node-body").Select(state => Pass(state).Id).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_child_completion_delivered_again_after_its_pass_committed_does_not_evaluate_the_condition_again()
    {
        // The second pass suspends, so the loop is still running when the first pass's completion arrives again.
        var clock = new ManualClock(Start);
        var plan = new PassPlan { SuspendOnPass = 2 };
        var observed = new ChildCompletionObserver();
        await using var harness = NewHarness(clock, plan, observed);
        var run = await harness.RunAsync(NewExecutable(While(Start.AddMinutes(1))));
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-loop").Status);
        var firstPass = Assert.Single(run.States("node-body"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Single(run.States("node-body"), state => state.Status == ActivityExecutionStatus.Suspended);
        // The first pass's completion, exactly as the drain dispatched it to the loop.
        var redelivered = Assert.Single(
            observed.WorkItems,
            item => item.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!.CompletedChildActivityExecutionId ==
                    firstPass.Execution.ActivityExecutionId);
        Assert.Equal(redelivered.WorkItemId, firstPass.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var commitCount = commits.ListCommits().Count;
        clock.Advance(TimeSpan.FromSeconds(30));

        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var handler = Assert.Single(
                scope.ServiceProvider.GetServices<IWorkflowSchedulerWorkHandler>(),
                candidate => candidate.Name == "WorkflowParentActivityCompletionSchedulerWorkHandler");
            Assert.True(handler.CanHandle(redelivered));
            await handler.HandleAsync(redelivered);
        }

        // Nothing was evaluated: no commit, no new pass, and the loop is still waiting on its second pass.
        Assert.Equal(commitCount, commits.ListCommits().Count);
        var after = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(2, after.States("node-body").Count);
        Assert.Equal(ActivityExecutionStatus.Running, after.State("node-loop").Status);
        Assert.Equal(2, plan.Passes);
    }

    [Fact]
    public async Task A_do_post_test_reads_each_passes_own_instant_and_the_body_id_differs_per_pass()
    {
        var clock = new ManualClock(Start);
        await using var harness = NewHarness(clock, new PassPlan());

        // The first pass is unconditional at Start; the post-test checks at Start+1s and Start+2s hold, Start+3s does not.
        var run = await harness.RunAsync(NewExecutable(Do(Start.AddSeconds(2.5))));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-loop", ActivityOutcomes.Done);
        var passes = run.States("node-body").Select(Pass).ToArray();
        Assert.Equal([Iso(Start), Iso(Start.AddSeconds(1)), Iso(Start.AddSeconds(2))], passes.Select(pass => pass.Now));
        Assert.Equal(passes.Length, passes.Select(pass => pass.Id).Distinct(StringComparer.Ordinal).Count());
    }

    private static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static (string Now, string Id) Pass(ActivityExecutionState state)
    {
        var parts = state.InputSnapshot!.Values["text"].InlineValue!.Value.GetString()!.Split('|');
        Assert.Equal(2, parts.Length);
        return (parts[0], parts[1]);
    }

    private static RuntimeExpressionBinding JavaScript(string script, IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new("JavaScript", script, parameters: parameters);

    /// <summary>ISO UTC strings compare lexicographically, so the comparison needs no <c>Date</c>.</summary>
    private static RuntimeInputBinding BeforeDeadline(DateTimeOffset deadline) =>
        new(
            "Condition",
            new ValueTypeDescriptor("Boolean"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: JavaScript(
                "args.now < args.deadline",
                new Dictionary<string, ExpressionParameterBinding>
                {
                    ["now"] = new ClockExpressionParameterBinding(),
                    ["deadline"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(Iso(deadline))),
                }));

    private static ExecutableNode Body() =>
        new(
            executableNodeId: "node-body",
            authoredActivityId: "authored-body",
            activityType: typeof(ClockedPassActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/leaf",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "leaf" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["text"] = new(
                    "text",
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: JavaScript(
                        "args.now + '|' + args.id",
                        new Dictionary<string, ExpressionParameterBinding>
                        {
                            ["now"] = new ClockExpressionParameterBinding(),
                            ["id"] = new UniqueIdExpressionParameterBinding(),
                        }))
            },
            metadata: new Dictionary<string, string>());

    private static ExecutableNode While(DateTimeOffset deadline) =>
        Loop(typeof(WhileActivity), WhileActivity.BodySlotName, WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, deadline);

    private static ExecutableNode Do(DateTimeOffset deadline) =>
        Loop(typeof(DoActivity), DoActivity.BodySlotName, DoActivity.StructureKind, DoActivity.StructureSchemaVersion, deadline);

    private static ExecutableNode Loop(Type activityType, string bodySlotName, string structureKind, string structureSchemaVersion, DateTimeOffset deadline)
    {
        var body = Body();
        return new ExecutableNode(
            executableNodeId: "node-loop",
            authoredActivityId: "authored-loop",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(LoopDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new LoopDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["Condition"] = BeforeDeadline(deadline) },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(bodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                structureKind,
                structureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));
    }

    /// <summary>The harness's executable shape, plus the body's resume target for the pass that suspends.</summary>
    private static WorkflowExecutable NewExecutable(ExecutableNode root)
    {
        var resumeTargetId = $"resume:node-body:{ClockedPassActivity.ResumeTargetKey}";
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [resumeTargetId] = new(resumeTargetId, "node-body", "ResumeAsync", new Dictionary<string, string>(),
                    LocalResumeTargetId: ClockedPassActivity.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutionHarness NewHarness(ManualClock clock, PassPlan plan, ChildCompletionObserver? observer = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(clock);
                services.AddSingleton(plan);
                // First in line, so the drain asks it about every work item before the real handlers.
                if (observer is not null)
                    services.Insert(0, ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler>(observer));
            })
            .Build(ActivityExecutionIds);

        using var scope = harness.Services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

        return harness;
    }

    private sealed record LoopDescriptor;
}

/// <summary>
/// Observes the child-completion work items the drain dispatches without handling any: it answers <c>false</c> to
/// <see cref="CanHandle"/>, so the drain moves on to the real handler.
/// </summary>
public sealed class ChildCompletionObserver : IWorkflowSchedulerWorkHandler
{
    private readonly List<RuntimeSchedulerWorkItem> _workItems = [];
    public IReadOnlyList<RuntimeSchedulerWorkItem> WorkItems => _workItems;
    public string Name => nameof(ChildCompletionObserver);

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.CommandKind == WorkflowExecutionCommandKind.CompleteActivity &&
            workItem.Payload?.Deserialize<RuntimeCompleteActivityCommandPayload>()?.CompletedChildActivityExecutionId is not null &&
            _workItems.TrueForAll(seen => seen.WorkItemId != workItem.WorkItemId))
            _workItems.Add(workItem);
        return false;
    }

    public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The observer handles no work.");
}

/// <summary>A host clock that moves only when a test (or the loop body) advances it.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>How the loop body behaves: which pass (1-based) suspends instead of completing, and how many passes ran.</summary>
public sealed class PassPlan
{
    public int? SuspendOnPass { get; init; }
    public int Passes { get; set; }
}

public sealed record PassWaitState(int Pass);

public sealed record PassWaitTrigger(bool Released);

/// <summary>
/// A loop body hydrated with a <c>text</c> input built from <c>Clock</c> and <c>UniqueId</c>. Each pass advances the
/// host clock by one second after its inputs were materialized, so the loop's next re-check sees a later instant.
/// </summary>
public sealed class ClockedPassActivity(ManualClock clock, PassPlan plan) : StatefulActivity<ActivityUnit, PassWaitState, PassWaitTrigger>
{
    public const string ResumeTargetKey = "wait";

    [ActivityInput(Key = "text")]
    public string Text { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit, PassWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        plan.Passes++;
        clock.Advance(TimeSpan.FromSeconds(1));
        return ValueTask.FromResult(plan.Passes == plan.SuspendOnPass
            ? Suspend(new PassWaitState(plan.Passes), [new ActivityTriggerRegistration<PassWaitTrigger>(ResumeTargetKey, "TestPassWait", "pass:wait")])
            : Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }

    protected override ValueTask<ActivityTransition<ActivityUnit, PassWaitState>> ResumeAsync(ActivityResumeContext<PassWaitState, PassWaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}
