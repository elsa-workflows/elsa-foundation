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
// acquires the run as a drain does. When that replaces a lease already on the run, the job is told, and the
// alteration's own lease is left to expire so the run still shows that an owner was on it.
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
        Assert.False(harness.Handler.SawAReplacedLease);
        // Released: it replaced no lease, so it leaves none for a later reader to mistake for an owner.
        Assert.Null((await harness.OwnershipRecordAsync()).ExecutionLease);
        Assert.Null(harness.OwnershipContext.Current);
    }

    // An alteration during a held lease. It takes the run as a drain would, and the owner it replaced is refused
    // because a newer fencing token exists, not because the lease was released.
    [Fact]
    public async Task ActorCommand_OnARunAnotherOwnerHolds_TakesItOver_AndThatOwnerIsRefusedAtItsNextCommit()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        Assert.True(harness.Handler.SawAReplacedLease);
        using (harness.OwnershipContext.Push(drainLease))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
                await harness.Committer.CommitAsync(NewDrainCommit("drain-commit")));
        }

        // Not released: the run still shows that an owner was on it.
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-alteration", record.ExecutionLease!.OwnerId);
        Assert.Equal(2, record.ExecutionLease.FencingToken);
    }

    // An expired lease is how recovery finds a run whose owner stopped mid-drain. The alteration runs, and leaves an
    // expiring lease of its own in place of the one it replaced.
    [Fact]
    public async Task ActorCommand_OnARunWhoseOwnerStopped_Runs_AndLeavesALeaseForRecoveryToFind()
    {
        var harness = await LeasedHarness.CreateAsync();
        await harness.OwnershipOf("node-dead").AcquireAsync("workflow-1");
        harness.Time.Advance(TimeSpan.FromMinutes(2));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        Assert.True(harness.Handler.SawAReplacedLease);
        var left = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal("node-alteration", left.OwnerId);
        Assert.False(left.IsExpired(harness.Time.GetUtcNow()));
        harness.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(left.IsExpired(harness.Time.GetUtcNow()));
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

    // Only a loss after the commit is not a failure. Before it, the job was not done and must not be reported as done.
    [Fact]
    public async Task ActorCommand_ThatLosesTheRunWithoutCommitting_IsNotReportedAsDone()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(beforeCommit: () =>
            throw new RuntimeExecutionOwnershipLostException(harness!.OwnershipContext.Current!, "heartbeat", transitionStatus: null));

        await Assert.ThrowsAsync<RuntimeExecutionOwnershipLostException>(async () =>
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim)));

        Assert.Empty(harness.Checkpoints.ListCommits());
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
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
        Assert.False(harness.Handler.SawAReplacedLease);
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

    [Fact]
    public async Task ActorCommand_WithAStaleClaim_IsRefused_BeforeItTouchesTheRunsOwnership()
    {
        var harness = await LeasedHarness.CreateAsync();

        await Assert.ThrowsAsync<WorkflowAlterationClaimFenceException>(async () =>
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim, claimToken: "stale-token")));

        Assert.Empty(await harness.Liveness.ListAsync("workflow-1"));
        Assert.Equal(0, harness.Handler.CallCount);
    }

    // Whether a lease was replaced is known only to the runtime's own ownership service. With a replacement the
    // alteration runs as it did before this lease existed.
    [Fact]
    public async Task ActorCommand_WithAReplacementOwnershipService_RunsWithoutALease_AsBefore()
    {
        var harness = await LeasedHarness.CreateAsync(replacementOwnership: true);
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Null(Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence);
        Assert.Equal(drainLease.LeaseId, (await harness.OwnershipRecordAsync()).ExecutionLease!.LeaseId);
    }

    // The answer comes from the record revision the acquisition was written against: an owner that acquires after
    // the record was first read, and before it is written, is the lease this acquisition replaces.
    [Fact]
    public async Task AcquiringTheRun_ReportsALeaseAnOwnerTookBetweenItsReadAndItsWrite()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var drain = new RuntimeExecutionOwnershipService(liveness, new FakeTimeProvider(Now), new RuntimeExecutionOwnershipOptions { OwnerId = "node-drain" });
        // The service reads the clock between reading the record and writing it.
        var clock = new HookedClock(Now, () => drain.AcquireAsync("workflow-1").AsTask().GetAwaiter().GetResult());
        var alteration = new RuntimeExecutionOwnershipService(liveness, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-alteration" });

        var (lease, replacedALease) = await alteration.AcquireNotingReplacedLeaseAsync("workflow-1");

        Assert.True(replacedALease);
        Assert.Equal(2, lease.FencingToken);
        Assert.Equal("node-alteration", Assert.Single(await liveness.ListAsync("workflow-1")).ExecutionLease!.OwnerId);
    }

    [Fact]
    public async Task AcquiringTheRun_ReportsNoReplacedLease_WhenTheRunHadNone()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var time = new FakeTimeProvider(Now);
        var first = new RuntimeExecutionOwnershipService(liveness, time, new RuntimeExecutionOwnershipOptions { OwnerId = "node-first" });
        var second = new RuntimeExecutionOwnershipService(liveness, time, new RuntimeExecutionOwnershipOptions { OwnerId = "node-second" });

        var (firstLease, firstReplaced) = await first.AcquireNotingReplacedLeaseAsync("workflow-1");
        await first.ReleaseAsync(firstLease);
        var (_, secondReplaced) = await second.AcquireNotingReplacedLeaseAsync("workflow-1");

        Assert.False(firstReplaced);
        Assert.False(secondReplaced);
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
        private LeasedHarness(CompletingHandler handler, Func<ValueTask>? beforeCommit, Func<ValueTask>? afterCommit, bool replacementOwnership)
        {
            Handler = handler;
            Checkpoints = new InMemoryRuntimeCheckpointCommitStore(
                workflowExecutionStateStore: Workflows,
                operationalStateStore: Liveness,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
                timeProvider: Time,
                alterationStore: Alterations);
            var store = new HookedCommitStore(Checkpoints, beforeCommit, afterCommit);
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
            Func<ValueTask>? beforeCommit = null,
            Func<ValueTask>? afterCommit = null,
            bool replacementOwnership = false)
        {
            var harness = new LeasedHarness(handler ?? new CompletingHandler(), beforeCommit, afterCommit, replacementOwnership);
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

    private sealed class HookedCommitStore(IRuntimeCheckpointCommitStore inner, Func<ValueTask>? beforeCommit, Func<ValueTask>? afterCommit) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            if (beforeCommit is not null)
                await beforeCommit();
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (afterCommit is not null)
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

    // Runs a callback the first time the clock is read, which is between the service's read and its write.
    private sealed class HookedClock(DateTimeOffset now, Action onFirstRead) : TimeProvider
    {
        private Action? _onFirstRead = onFirstRead;

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Exchange(ref _onFirstRead, null)?.Invoke();
            return now;
        }
    }

    private sealed class UnusedHeartbeatScopes : IPersistenceOperationScopeFactory
    {
        public ValueTask<PersistenceOperationScope> CreateAsync(PersistenceScope persistenceScope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No heartbeat is due within these tests.");
    }
}
