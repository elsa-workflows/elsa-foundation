using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 261 R-7: a structural activity schedules a child not before a due time. The child's schedule commits in the
/// evaluation's checkpoint and becomes deliverable at the due time, so nothing of the run waits behind it; the outbox
/// sweep delivers it once it is due. Proven with a test structural activity, in every persistence mode (D1–D3, D5–D8).
/// </summary>
public sealed class DelayedScheduleExecutionTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(60);
    private static readonly string[] Ids = Enumerable.Range(1, 24).Select(index => $"actexec-{index}").ToArray();

    public static TheoryData<PersistenceMode> Modes => new() { PersistenceMode.Durable, PersistenceMode.Balanced, PersistenceMode.Ephemeral };

    // D1: a due time of zero or in the past schedules the child exactly as an immediate schedule does.
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task DelayedSchedule_AZeroOrPastDueTimeSchedulesAtOnce(int seconds)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [TimeSpan.FromSeconds(seconds)]));

        var run = await harness.RunAsync(NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"))));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-a");
        Assert.Empty(await PendingDelayedItemsAsync(harness));
    }

    // D2 + D7: the child is not scheduled before its due time and is scheduled once at the first sweep after it, in
    // every mode. The item is durable with its due time after the drain ends.
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_TheChildRunsOnceAtTheFirstSweepAfterItsDueTime(PersistenceMode mode)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [Delay]), mode);
        var startedAt = clock.GetUtcNow();

        var run = await harness.RunAsync(NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"))));

        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);
        Assert.False(run.DidRun("node-a"));
        var pending = Assert.Single(await PendingDelayedItemsAsync(harness));
        Assert.InRange(pending.AvailableAt!.Value, startedAt + Delay, clock.GetUtcNow() + Delay);

        clock.Advance(Delay - TimeSpan.FromSeconds(2));
        await harness.SweepUntilQuietAsync();
        Assert.False((await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId)).DidRun("node-a"));

        clock.Advance(TimeSpan.FromSeconds(3));
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-a"));
    }

    // D3: while one child waits for its due time, the run's other work goes on at once.
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_OtherWorkOfTheRunIsNotHeldBack(PersistenceMode mode)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [Delay, null]), mode);

        var run = await harness.RunAsync(NewExecutable(Parent(
            WorkflowExecutionHarness.NewProbeNode("node-a"),
            WorkflowExecutionHarness.NewProbeNode("node-b"))));

        run.AssertCompleted("node-b");
        Assert.False(run.DidRun("node-a"));
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);
    }

    // The child-completion evaluation may delay too: the parent reschedules its child after the first completion.
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_AChildCompletionEvaluationCanDelayTheNextSchedule(PersistenceMode mode)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [null], RescheduleDelay: Delay), mode);

        var run = await harness.RunAsync(NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"))));
        Assert.Single(run.States("node-a"));
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);

        clock.Advance(Delay + TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        Assert.Equal(2, run.States("node-a").Count);
    }

    // D5: the parent finished before the due time; the delivered schedule is acknowledged and creates no child.
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_AParentThatIsNoLongerRunningGetsNoChild(PersistenceMode mode)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [Delay, null], CompleteOn: "node-b"), mode);

        // A holder keeps the run running on a waiting child after the delayed parent completes.
        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-holder", typeof(HoldAllActivity),
                Parent(WorkflowExecutionHarness.NewProbeNode("node-a"), WorkflowExecutionHarness.NewProbeNode("node-b")),
                StructuralExecutionTestSupport.NewWaitingNode("node-wait")),
            waitingNodeIds: ["node-wait"]));
        run.AssertCompleted("node-parent");
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);

        clock.Advance(Delay + TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.False(run.DidRun("node-a"));
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Empty(await PendingDelayedItemsAsync(harness));
    }

    // D5: the run was cancelled, or faulted, before the due time.
    [Theory]
    [InlineData(PersistenceMode.Durable, false)]
    [InlineData(PersistenceMode.Balanced, false)]
    [InlineData(PersistenceMode.Ephemeral, false)]
    [InlineData(PersistenceMode.Durable, true)]
    [InlineData(PersistenceMode.Balanced, true)]
    [InlineData(PersistenceMode.Ephemeral, true)]
    public async Task DelayedSchedule_ATerminalRunGetsNoChild(PersistenceMode mode, bool faulted)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [Delay, null]), mode);
        var second = faulted ? WorkflowExecutionHarness.NewFaultingNode("node-b") : StructuralExecutionTestSupport.NewWaitingNode("node-b");
        var reference = await harness.PublishAsync(
            StructuralExecutionTestSupport.NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"), second), waitingNodeIds: faulted ? null : ["node-b"]),
            "source-delayed");
        await harness.StartPublishedAsync(reference, WorkflowExecutionHarness.WorkflowExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
        if (!faulted)
            await harness.CancelAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(faulted ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Cancelled, run.WorkflowState!.Status);

        clock.Advance(Delay + TimeSpan.FromHours(1));
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.False(run.DidRun("node-a"));
    }

    // D5: a run deleted after it was cancelled; the delivered schedule finds no run and creates nothing.
    [Fact]
    public async Task DelayedSchedule_ADeletedRunGetsNoChild()
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [Delay, null]));
        var reference = await harness.PublishAsync(
            StructuralExecutionTestSupport.NewExecutable(
                Parent(WorkflowExecutionHarness.NewProbeNode("node-a"), StructuralExecutionTestSupport.NewWaitingNode("node-b")),
                waitingNodeIds: ["node-b"]),
            "source-delayed");
        await harness.StartPublishedAsync(reference, WorkflowExecutionHarness.WorkflowExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
        await harness.CancelAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        var deletion = harness.Services.GetRequiredService<IWorkflowExecutionDeletionStore>();
        var candidate = await deletion.ReadAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.NotNull(candidate);
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate));

        clock.Advance(Delay + TimeSpan.FromHours(1));
        await harness.SweepUntilQuietAsync();
        await harness.SweepUntilQuietAsync();

        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    // D6: a due time beyond the limit, or a host without the outbox sweep, faults the evaluation with a stable code and
    // commits no schedule.
    [Theory]
    [InlineData(true, RuntimeDelayedScheduleFaultCodes.TooFar)]
    [InlineData(false, RuntimeDelayedScheduleFaultCodes.Unavailable)]
    public async Task DelayedSchedule_ARefusedDueTimeFaultsTheEvaluation(bool capability, string code)
    {
        var clock = new ShiftableTimeProvider();
        var delay = capability ? RuntimeDelayedScheduleOptions.DefaultMaxDelay + TimeSpan.FromSeconds(1) : Delay;
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [delay]), capability: capability);

        var run = await harness.RunAsync(
            NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal(code, parent.Fault?.Code);
        Assert.False(run.DidRun("node-a"));
        Assert.Empty(await PendingDelayedItemsAsync(harness));
    }

    // D8: a run whose only outstanding work is a delayed schedule stays running, is no recovery candidate, and no sweep
    // re-drives it before the due time.
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_ARunThatOnlyWaitsIsNotStalled(PersistenceMode mode)
    {
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(clock, new DelayedScheduleDirective(InitialDelays: [TimeSpan.FromHours(2)]), mode);
        await harness.RunAsync(NewExecutable(Parent(WorkflowExecutionHarness.NewProbeNode("node-a"))));

        // Well past every lease and heartbeat timeout, still before the due time.
        clock.Advance(TimeSpan.FromHours(1));
        var candidates = await harness.Services.GetRequiredService<IRuntimeRecoveryScanner>().ScanAsync(
            new RuntimeRecoveryScanRequest(clock.GetUtcNow(), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), limit: 10));
        Assert.Empty(candidates);
        var sweep = await harness.SweepAsync();
        Assert.Equal(0, sweep.OutboxAttemptedCount);
        Assert.Empty(sweep.Dispatches);

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.False(run.DidRun("node-a"));
        Assert.Single(await PendingDelayedItemsAsync(harness));
    }

    private static async Task<IReadOnlyCollection<RuntimePostCommitOutboxItem>> PendingDelayedItemsAsync(WorkflowExecutionHarness harness)
    {
        var store = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var delayedIds = store.ListCommits()
            .SelectMany(record => record.Commit.StateChanges.PostCommitOutbox)
            .Where(change => change.State.Intent.NotBefore is not null)
            .Select(change => change.StateId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pending = new List<RuntimePostCommitOutboxItem>();
        foreach (var id in delayedIds)
            if (await store.FindAsync(id) is { Status: RuntimePostCommitOutboxStatus.Pending } item)
                pending.Add(item);
        return pending;
    }

    private static WorkflowExecutionHarness NewHarness(
        TimeProvider clock,
        DelayedScheduleDirective directive,
        PersistenceMode mode = PersistenceMode.Durable,
        bool capability = true)
    {
        var builder = WorkflowExecutionHarness.Create();
        if (mode != PersistenceMode.Durable)
            builder = builder.WithPersistenceMode(mode);
        return builder
            .ConfigureServices(services =>
            {
                services.AddSingleton(directive);
                services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                if (capability)
                    services.AddSingleton<IRuntimeDelayedScheduleCapability, TestDelayedScheduleCapability>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            })
            .WithProbeLeaf()
            .Build(Ids);
    }

    private static WorkflowExecutable NewExecutable(ExecutableNode root) => StructuralExecutionTestSupport.NewExecutable(root);

    private static ExecutableNode Parent(params ExecutableNode[] children) =>
        StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(DelayedSchedulingActivity), children);

    private sealed class TestDelayedScheduleCapability : IRuntimeDelayedScheduleCapability;

    private sealed class ShiftableTimeProvider : TimeProvider
    {
        private TimeSpan _offset;

        public void Advance(TimeSpan by) => _offset += by;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;
    }
}

/// <summary>
/// How <see cref="DelayedSchedulingActivity"/> schedules: a delay per child for its first schedule (null: at once), an
/// optional delay to schedule the first child again after its first completion, and an optional child whose
/// completion completes the parent at once.
/// </summary>
public sealed record DelayedScheduleDirective(TimeSpan?[] InitialDelays, TimeSpan? RescheduleDelay = null, string? CompleteOn = null);

/// <summary>A test structural activity that schedules its children with the due times its directive gives.</summary>
public sealed class DelayedSchedulingActivity(DelayedScheduleDirective directive, TimeProvider clock) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler
{
    private const string RescheduledKey = "test.rescheduled";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var children = Children(context);
        for (var index = 0; index < children.Count; index++)
            Schedule(context, children[index], directive.InitialDelays.ElementAtOrDefault(index), rescheduled: false);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
        if (StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, directive.CompleteOn))
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        var children = Children(runtimeContext);
        var rescheduled = runtimeContext.SchedulerWorkItem.CommandMetadata.ContainsKey(RescheduledKey);
        if (directive.RescheduleDelay is { } delay && !rescheduled && StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, children[0].ExecutableNodeId))
        {
            Schedule(runtimeContext, children[0], delay, rescheduled: true);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        // The first child decides: the parent completes when it has run (again, when it was rescheduled).
        return ValueTask.FromResult(StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, children[0].ExecutableNodeId)
            ? RuntimeStructuralContinuation.Complete()
            : RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

    private void Schedule(IRuntimeActivityExecutionContext context, ExecutableNode child, TimeSpan? delay, bool rescheduled) =>
        context.ScheduleChildActivity(
            child.ExecutableNodeId,
            context.ActivityExecutionState.InvocationId,
            rescheduled ? new Dictionary<string, string> { [RescheduledKey] = "true" } : null,
            schedulingProvenance: null,
            iterationFrame: null,
            notBefore: delay is { } value ? clock.GetUtcNow() + value : null);

    private static IReadOnlyList<ExecutableNode> Children(IRuntimeActivityExecutionContext context) =>
        Assert.Single(context.ExecutableNode.ChildSlots).Activities.ToArray();
}

/// <summary>Schedules every child at once and completes when all of them completed.</summary>
public sealed class HoldAllActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
}
