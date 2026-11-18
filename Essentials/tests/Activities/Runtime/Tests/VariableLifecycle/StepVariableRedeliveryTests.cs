using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Activities.Runtime.Tests.PersistenceModeTestRuns;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 T016 (SC-003): a host that stops after a step's body ran but before its completion was durable recovers with
/// a new attempt and a new session. The lost attempt's intents were never committed, so the recovered attempt reads the
/// committed value again and its write applies exactly once.
/// </summary>
public sealed class StepVariableRedeliveryTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task A_redelivered_step_reads_committed_values_again_and_applies_its_write_once()
    {
        var probe = new StopProbe();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                services.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(serviceProvider =>
                    new StoppingCommitStore(serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(), probe)));
            })
            .Build(Ids);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(StepVariableTestGraph.Parallel(
            StepVariableTestGraph.CountLeaf("node-left", typeof(ProbingSessionIncrementActivity)),
            StepVariableTestGraph.CountLeaf("node-right", typeof(ProbingSessionIncrementActivity)))));
        Assert.True(probe.Stopped);

        await DeliverAsync(harness, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionActorActivationReason.Recovery);

        var run = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        // Each branch's increment applied once: 2, not 3.
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var left = run.State("node-left");
        Assert.Equal(2, left.Attempts!.Count);
        // Only the recovered attempt's session read was committed; the lost attempt's reads and intents never were.
        Assert.Equal(left.Attempts.Last().AttemptId, Assert.Single(left.SessionReads!).AttemptId);
        Assert.Single(left.VariableWrites!);
    }

    public sealed class StopProbe
    {
        private int _bodyRuns;
        private int _stopped;
        public bool Stopped => Volatile.Read(ref _stopped) == 1;
        internal void RecordBodyRun() => Interlocked.Increment(ref _bodyRuns);

        // The first durable commit after the first body ran stops the host: the body ran, its completion is not durable.
        internal bool TakeStop() => Volatile.Read(ref _bodyRuns) >= 1 && Interlocked.Exchange(ref _stopped, 1) == 0;
    }

    private sealed class StoppingCommitStore(InMemoryRuntimeCheckpointCommitStore inner, StopProbe probe) : IRuntimeCheckpointCommitStore
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

/// <summary>A session increment that tells the probe its body ran.</summary>
public sealed class ProbingSessionIncrementActivity(StepVariableRedeliveryTests.StopProbe probe) : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, context.Variables.Get(Count) + 1);
        probe.RecordBodyRun();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}
