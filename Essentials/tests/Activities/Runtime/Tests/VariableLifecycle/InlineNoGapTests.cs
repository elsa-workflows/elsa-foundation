using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Activities.Runtime.Tests.PersistenceModeTestRuns;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// T025 / T027 (spec 1221, US4, Open question A, FR-021, SC-004). An inline leaf's input materialization, body and
/// transition commit run in one fused span, so no other branch's work item runs in between, for <c>External</c> and
/// <c>ReplaySafe</c> leaves alike. For an <c>External</c> leaf the attempt claim is still a durable intermediate commit
/// before the body runs, the span's Schedule work item is durable with it, and a host that stops after the body but
/// before the completion is durable recovers the run through the existing redelivery ladder with a second attempt on
/// the same invocation and input snapshot. No transaction spans the body: the claim is committed before the body starts.
/// </summary>
public sealed class InlineNoGapTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Theory]
    [InlineData(typeof(IncrementActivity))]
    [InlineData(typeof(ReplaySafeIncrementActivity))]
    public async Task Two_parallel_inline_increments_total_two(Type incrementActivity)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(incrementActivity));

        run.AssertRan("node-left", "node-right");
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        // The right leaf read the left leaf's committed write: nothing ran between its input read and its commit.
        Assert.Equal(1, run.State("node-right").InputSnapshot!.Values["Count"].InlineValue!.Value.GetInt32());
        Assert.True(harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>().FusedSpans >= 2);
    }

    [Fact]
    public async Task External_leaf_claim_is_durable_before_its_body_and_a_stop_before_its_completion_recovers_with_a_new_attempt()
    {
        var probe = new ClaimProbe();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                services.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(serviceProvider =>
                    new HostStoppingCommitStore(serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(), probe)));
            })
            .Build(Ids);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(ProbingIncrementActivity))));

        // When the body first ran, its attempt claim and the span's Schedule work item were already durable.
        Assert.True(probe.ClaimWasDurableAtFirstBody, "The External attempt claim was not durable when the body ran.");
        Assert.True(probe.ScheduleWasDurableAtFirstBody, "The fused span's Schedule work item was not durable when the body ran.");
        Assert.True(probe.Stopped);

        await DeliverAsync(harness, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionActorActivationReason.Recovery);

        var run = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        // Recovery keeps the invocation and its committed input snapshot; the stopped attempt is followed by a new one,
        // so the External body runs again (at least once), as it does on the discrete path.
        var left = run.State("node-left");
        Assert.Equal("actexec-left", left.InvocationId);
        Assert.Equal(0, left.InputSnapshot!.Values["Count"].InlineValue!.Value.GetInt32());
        Assert.Equal(2, left.Attempts.Count);
        run.AssertCompleted("node-right");
        Assert.Empty(await QueuedWorkAsync(harness));
    }

    /// <summary>What the probing leaf saw of the durable stores the first time a body ran.</summary>
    public sealed class ClaimProbe
    {
        private int _bodyRuns;
        private int _stopped;
        public int BodyRuns => Volatile.Read(ref _bodyRuns);
        public bool ClaimWasDurableAtFirstBody { get; private set; }
        public bool ScheduleWasDurableAtFirstBody { get; private set; }
        public bool Stopped => Volatile.Read(ref _stopped) == 1;

        internal void RecordBodyRun(IServiceProvider services, string activityExecutionId, string workflowExecutionId)
        {
            if (Interlocked.Increment(ref _bodyRuns) != 1)
                return;

            // The in-memory commit store and queue are the durable layer under the coalescing session.
            var commits = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits();
            ClaimWasDurableAtFirstBody = commits.Any(record =>
                record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed &&
                record.Commit.Checkpoint.ActivityExecutionIds.Contains(activityExecutionId));
            var durableQueue = services.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
            ScheduleWasDurableAtFirstBody = durableQueue.ListAllAsync(workflowExecutionId).AsTask().GetAwaiter().GetResult()
                .Any(item => item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
                             item.Payload is { } payload &&
                             payload.GetProperty(nameof(RuntimeScheduleActivityCommandPayload.ActivityExecutionId)).GetString() == activityExecutionId);
        }

        // The first durable commit after the first body ran stops the host: the body ran, its completion is not durable.
        internal bool TakeStop() => BodyRuns >= 1 && Interlocked.Exchange(ref _stopped, 1) == 0;
    }

    private sealed class HostStoppingCommitStore(InMemoryRuntimeCheckpointCommitStore inner, ClaimProbe probe) : IRuntimeCheckpointCommitStore
    {
        private IRuntimeCheckpointCommitStore Inner => inner;
        public bool SupportsExactBookmarkResume => Inner.SupportsExactBookmarkResume;

        public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore physicalExecutions, IActivityExecutionStateStore activities) =>
            Inner.CanCommitExactBookmarkResume(bookmarks, physicalExecutions, activities);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            probe.TakeStop()
                ? throw new OperationCanceledException("The host stopped before the completion was durable.")
                : Inner.CommitAsync(commit, decision, cancellationToken);
    }
}

/// <summary>An External increment that reports, on the first body run, what was durable at that moment.</summary>
public sealed class ProbingIncrementActivity(InlineNoGapTests.ClaimProbe probe, IServiceProvider services) : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        probe.RecordBodyRun(services, context.InvocationId, context.WorkflowExecutionId);
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
    }
}
