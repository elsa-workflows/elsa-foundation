using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

// #2667: an alteration reads its run and commits under the run's ownership lease, so its checkpoint is fenced. It
// acquires the run only while no live lease holds it, and otherwise waits for its next delivery.
public sealed partial class WorkflowAlterationActorIntegrationTests
{
    [Fact]
    public async Task ActorCommand_CommitsUnderTheRunsOwnershipLease_AndReleasesIt()
    {
        var harness = await LeasedHarness.CreateAsync();

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        var commit = Assert.Single(harness.Checkpoints.ListCommits()).Commit;
        Assert.NotNull(commit.ExpectedFence);
        Assert.Equal(1, commit.ExpectedFence!.FencingToken);
        Assert.Equal("node-alteration", commit.ExpectedFence.OwnerId);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        // Released: no lease is left for a later drain or alteration to mistake for a live owner.
        Assert.Null((await harness.OwnershipRecordAsync()).ExecutionLease);
        Assert.Null(harness.OwnershipContext.Current);
    }

    // An alteration during a held lease: it waits, as it waits behind a drain's mailbox turn on one node.
    [Fact]
    public async Task ActorCommand_WhileALiveOwnerHoldsTheRun_DoesNothing_AndRunsWhenDeliveredAgain()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drain = harness.OwnershipOf("node-drain");
        var drainLease = await drain.AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(0, harness.Handler.CallCount);
        Assert.Empty(harness.Checkpoints.ListCommits());
        var job = (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Running, job.Status);
        Assert.Equal(harness.Claim.Claim!.Token, job.Claim!.Token);
        // The drain still owns the run, so its next commit lands.
        Assert.Equal(drainLease.LeaseId, (await harness.OwnershipRecordAsync()).ExecutionLease!.LeaseId);
        using (harness.OwnershipContext.Push(drainLease))
            await harness.Committer.CommitAsync(NewDrainCommit("drain-commit"));

        await drain.ReleaseAsync(drainLease);
        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(1, harness.Handler.CallCount);
        Assert.Equal(2, harness.Checkpoints.ListCommits().Single(record => record.Commit.StateChanges.AlterationJobTerminalChange is not null).Commit.ExpectedFence!.FencingToken);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
    }

    // A lease that has expired is a dead owner's. The alteration takes the run, and the dead owner is fenced out.
    [Fact]
    public async Task ActorCommand_TakesARunWhoseOwnersLeaseHasExpired()
    {
        var harness = await LeasedHarness.CreateAsync();
        var deadLease = await harness.OwnershipOf("node-dead").AcquireAsync("workflow-1");
        harness.Time.Advance(TimeSpan.FromMinutes(2));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
        using (harness.OwnershipContext.Push(deadLease))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
                await harness.Committer.CommitAsync(NewDrainCommit("dead-owner-commit")));
        }
    }

    // An alteration racing a drain on two nodes: the drain takes the run while the alteration is still deciding.
    [Fact]
    public async Task ActorCommand_ThatLosesTheRunBeforeItCommits_IsRefused_AndItsJobStaysClaimed()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async () =>
            await harness!.OwnershipOf("node-drain").AcquireAsync("workflow-1")));

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim)));

        Assert.Empty(harness.Checkpoints.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await harness.Workflows.FindAsync("workflow-1"))!.Status);
        var job = (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Running, job.Status);
        Assert.Equal(harness.Claim.Claim!.Token, job.Claim!.Token);
        // The new owner's lease is untouched by the loser's release.
        Assert.Equal("node-drain", (await harness.OwnershipRecordAsync()).ExecutionLease!.OwnerId);
    }

    // The alteration committed under its fence. Losing the run before the release is not a failure of the job.
    [Fact]
    public async Task ActorCommand_ThatLosesTheRunAfterItCommitted_Succeeds()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(afterCommit: async () =>
            await harness!.OwnershipOf("node-drain").AcquireAsync("workflow-1"));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        Assert.Equal("node-drain", (await harness.OwnershipRecordAsync()).ExecutionLease!.OwnerId);
    }

    [Fact]
    public async Task ActorCommand_InsideAFlowThatHoldsTheRun_RunsUnderThatLease()
    {
        var harness = await LeasedHarness.CreateAsync();
        var held = await harness.OwnershipOf("node-alteration").AcquireAsync("workflow-1");

        using (harness.OwnershipContext.Push(held))
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        var fence = Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!;
        Assert.Equal(held.LeaseId, fence.LeaseId);
        Assert.Equal(1, fence.FencingToken);
        // Still held: the caller that acquired it releases it.
        Assert.Equal(held.LeaseId, (await harness.OwnershipRecordAsync()).ExecutionLease!.LeaseId);
    }

    // A duplicate after an acknowledgement loss does nothing, so it must not touch the run's ownership either.
    [Fact]
    public async Task ActorCommand_DuplicateOfAFinishedJob_LeavesTheRunsOwnershipAlone()
    {
        var harness = await LeasedHarness.CreateAsync();
        var envelope = NewEnvelope(harness.Claim);
        await harness.Executor.ExecuteAsync(envelope);
        var before = await harness.Liveness.FindVersionedAsync("workflow-1", (await harness.OwnershipRecordAsync()).ExecutionLivenessStateId);

        await harness.Executor.ExecuteAsync(envelope);

        var after = await harness.Liveness.FindVersionedAsync("workflow-1", before!.State.ExecutionLivenessStateId);
        Assert.Equal(before.Revision, after!.Revision);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    // A replacement ownership service cannot acquire only when unheld, so the alteration acquires as a drain does.
    [Fact]
    public async Task ActorCommand_WithAReplacementOwnershipService_AcquiresAsADrainDoes()
    {
        var harness = await LeasedHarness.CreateAsync(replacementOwnership: true);
        await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
    }

    [Fact]
    public void RuntimeCore_ComposesAnExecutorThatHoldsTheRunLease()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = Assert.IsType<WorkflowAlterationActorCommandExecutor>(
            scope.ServiceProvider.GetRequiredService<IWorkflowAlterationActorCommandExecutor>());

        Assert.True(executor.HoldsRunLease);
    }

    private sealed class LeasedHarness
    {
        private LeasedHarness(CompletingHandler handler, Func<ValueTask>? afterCommit, bool replacementOwnership)
        {
            Handler = handler;
            Checkpoints = new InMemoryRuntimeCheckpointCommitStore(
                workflowExecutionStateStore: Workflows,
                operationalStateStore: Liveness,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
                timeProvider: Time,
                alterationStore: Alterations);
            IRuntimeCheckpointCommitStore store = afterCommit is null ? Checkpoints : new AfterCommitStore(Checkpoints, afterCommit);
            Committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), store, OwnershipContext, [], []);
            var writer = new RuntimeWorkflowAlterationCheckpointWriter(Committer, Alterations, Time);
            IRuntimeExecutionOwnershipService ownership = OwnershipOf("node-alteration");
            if (replacementOwnership)
                ownership = new ReplacementOwnershipService(ownership);
            Executor = new WorkflowAlterationActorCommandExecutor(
                Alterations,
                NewProtector(),
                Workflows,
                new WorkflowAlterationJobExecutor(new SingleHandlerResolver(handler), writer, Time),
                activityExecutionStateStore: null,
                schedulerWorkQueue: null,
                Time,
                new AlterationRunLease(ownership, OwnershipContext, new UnusedHeartbeatScopes(), Time));
        }

        public FakeTimeProvider Time { get; } = new(Now);
        public CompletingHandler Handler { get; }
        public InMemoryWorkflowAlterationStore Alterations { get; } = new();
        public InMemoryWorkflowExecutionStateStore Workflows { get; } = new();
        public InMemoryExecutionLivenessStateStore Liveness { get; } = new();
        public AsyncLocalRuntimeExecutionOwnershipContextAccessor OwnershipContext { get; } = new();
        public InMemoryRuntimeCheckpointCommitStore Checkpoints { get; }
        public RuntimeCheckpointCommitter Committer { get; }
        public WorkflowAlterationActorCommandExecutor Executor { get; }
        public WorkflowAlterationJobState Claim { get; private set; } = null!;

        public RuntimeExecutionOwnershipService OwnershipOf(string ownerId) =>
            new(Liveness, Time, new RuntimeExecutionOwnershipOptions { OwnerId = ownerId });

        public async Task<ExecutionLivenessState> OwnershipRecordAsync() =>
            Assert.Single(await Liveness.ListAsync("workflow-1"));

        public static async Task<LeasedHarness> CreateAsync(
            CompletingHandler? handler = null,
            Func<ValueTask>? afterCommit = null,
            bool replacementOwnership = false)
        {
            var harness = new LeasedHarness(handler ?? new CompletingHandler(), afterCommit, replacementOwnership);
            await harness.Workflows.SaveAsync(NewWorkflow());
            harness.Claim = await AdmitSealAndClaimAsync(harness.Alterations);
            return harness;
        }
    }

    // What a drain would commit next: any checkpoint of the run, carrying the drain's ambient lease as its fence.
    private static RuntimeCheckpointCommit NewDrainCommit(string commitId) =>
        new(
            commitId,
            new RuntimeCheckpoint($"checkpoint:{commitId}", RuntimeCheckpointNames.ActivityCompleted, "workflow-1", Now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            [],
            new Dictionary<string, string>());

    private sealed class AfterCommitStore(IRuntimeCheckpointCommitStore inner, Func<ValueTask> afterCommit) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            await afterCommit();
            return result;
        }
    }

    // Any implementation other than the runtime's own: only the contract's operations are available.
    private sealed class ReplacementOwnershipService(IRuntimeExecutionOwnershipService inner) : IRuntimeExecutionOwnershipService
    {
        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(workflowExecutionId, cancellationToken);

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(lease, cancellationToken);

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) =>
            inner.EnsureCurrentAsync(workflowExecutionId, fencingToken, cancellationToken);
    }

    private sealed class UnusedHeartbeatScopes : IPersistenceOperationScopeFactory
    {
        public ValueTask<PersistenceOperationScope> CreateAsync(PersistenceScope persistenceScope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No heartbeat is due within these tests.");
    }
}
