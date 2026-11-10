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
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

// #2667: an alteration reads its run and commits under the run's ownership lease, so its checkpoint is fenced. It
// acquires the run as a drain does, but it borrows the lease: when the run had an owner, that owner's lease is given
// back under a new fencing token once the alteration is done.
public sealed partial class WorkflowAlterationActorIntegrationTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ActorCommand_CommitsUnderTheRunsOwnershipLease_AndReleasesIt()
    {
        WorkflowMigrationQuiescenceReport? quiescence = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ => quiescence = await harness!.ProbeAsOwnerAsync()));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        var commit = Assert.Single(harness.Checkpoints.ListCommits()).Commit;
        Assert.NotNull(commit.ExpectedFence);
        Assert.Equal(1, commit.ExpectedFence!.FencingToken);
        Assert.Equal("node-alteration", commit.ExpectedFence.OwnerId);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        // No owner was on the run, so the alteration's own lease does not make the run busy.
        Assert.True(quiescence!.IsQuiescent);
        // Released: it leaves no lease for a later reader to mistake for an owner.
        Assert.Null((await harness.OwnershipRecordAsync()).ExecutionLease);
        Assert.Null((await harness.OwnershipRecordAsync()).Heartbeat);
        Assert.Null(harness.OwnershipContext.Current);
    }

    // An alteration during a held lease. It takes the run as a drain would, the owner it took the run from is refused,
    // and the ownership record afterwards names that owner again.
    [Fact]
    public async Task ActorCommand_OnARunAnotherOwnerHolds_TakesItOver_AndGivesThatOwnersLeaseBackFencedOut()
    {
        WorkflowMigrationQuiescenceReport? quiescence = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ => quiescence = await harness!.ProbeAsOwnerAsync()));
        var drain = harness.OwnershipOf("node-drain");
        var drainLease = await drain.AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        // Spec 141 FR-032: a migration in this job would have been refused, although the probe exempts the caller's lease.
        Assert.Contains(quiescence!.Findings, finding => finding.Code == "ActiveExecutionFence");

        // The owner is fenced out of both its heartbeat and its next commit.
        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, (await drain.HeartbeatAsync(drainLease)).Status);
        using (harness.OwnershipContext.Push(drainLease))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
                await harness.Committer.CommitAsync(NewDrainCommit("drain-commit")));
        }

        // Its lease is back as it was, under a token it does not hold: host reclaim and recovery read these fields.
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-drain", record.ExecutionLease!.OwnerId);
        Assert.Equal(drainLease.LeaseId, record.ExecutionLease.LeaseId);
        Assert.Equal(drainLease.AcquiredAt, record.ExecutionLease.AcquiredAt);
        Assert.Equal(drainLease.ExpiresAt, record.ExecutionLease.ExpiresAt);
        Assert.Equal(3, record.ExecutionLease.FencingToken);
        Assert.Equal("node-drain", record.Heartbeat!.OwnerId);
        Assert.Equal(drainLease.LeaseId, record.Heartbeat.LeaseId);
    }

    // An expired lease is how recovery finds a run whose owner stopped mid-drain. The alteration runs, and recovery
    // still finds the run under that owner, no later than it would have.
    [Fact]
    public async Task ActorCommand_OnARunWhoseOwnerStopped_Runs_AndRecoveryStillFindsTheRunUnderThatOwner()
    {
        var harness = await LeasedHarness.CreateAsync();
        var deadLease = await harness.OwnershipOf("node-dead").AcquireAsync("workflow-1");
        harness.Time.Advance(TimeSpan.FromMinutes(2));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal(deadLease.ExpiresAt, record.ExecutionLease!.ExpiresAt);
        var candidate = Assert.Single(RuntimeRecoveryCandidateSelector.Select(
            [record],
            new RuntimeRecoveryScanRequest(harness.Time.GetUtcNow(), LeaseDuration, LeaseDuration, limit: 10, ownerId: "node-dead")));
        Assert.Equal(RuntimeInterruptionReason.LeaseLost, candidate.Reason);
    }

    // An alteration that stopped without releasing left a lease, but no owner was ever on the run. The next alteration
    // sees none, and its release clears the record.
    [Fact]
    public async Task ActorCommand_AfterAnAlterationThatStoppedWithoutReleasing_FindsNoOwner_AndClearsTheLease()
    {
        WorkflowMigrationQuiescenceReport? quiescence = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ => quiescence = await harness!.ProbeAsOwnerAsync()));
        await harness.OwnershipOf("node-stopped").BorrowAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(2, Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!.FencingToken);
        Assert.True(quiescence!.IsQuiescent);
        Assert.Null((await harness.OwnershipRecordAsync()).ExecutionLease);
    }

    // An alteration racing a drain on two nodes: the drain takes the run while the alteration is still deciding.
    [Fact]
    public async Task ActorCommand_ThatLosesTheRunBeforeItCommits_IsRefused_AndItsJobStaysClaimed()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ =>
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

    // The same loss, found by the lease's renewal instead of by the commit: the alteration is cancelled where it stands.
    [Fact]
    public async Task ActorCommand_WhoseRenewalFindsTheRunTaken_IsCancelled_AndItsJobStaysClaimed()
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async leasedCancellation =>
        {
            await harness!.OwnershipOf("node-drain").AcquireAsync("workflow-1");
            harness.Time.Advance(LeaseDuration / 3);
            await Task.Delay(Timeout.InfiniteTimeSpan, leasedCancellation).WaitAsync(TimeSpan.FromSeconds(30));
        }));

        await Assert.ThrowsAsync<RuntimeExecutionOwnershipLostException>(async () =>
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim)));

        Assert.Empty(harness.Checkpoints.ListCommits());
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        Assert.Equal("node-drain", (await harness.OwnershipRecordAsync()).ExecutionLease!.OwnerId);
    }

    // A renewal rewrites the lease. It must still say that it stands in for an owner, and the owner's lease that is
    // given back must be the one that was found, not the renewed one.
    [Fact]
    public async Task ActorCommand_ThatOutlastsARenewal_StillStandsInForTheOwner_AndGivesBackTheLeaseItFound()
    {
        WorkflowMigrationQuiescenceReport? quiescence = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ =>
        {
            var borrowed = (await harness!.OwnershipRecordAsync()).ExecutionLease!;
            harness.Time.Advance(LeaseDuration / 3);
            await WaitUntilAsync(async () => (await harness.OwnershipRecordAsync()).ExecutionLease!.ExpiresAt > borrowed.ExpiresAt);
            quiescence = await harness.ProbeAsOwnerAsync();
        }));
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Contains(quiescence!.Findings, finding => finding.Code == "ActiveExecutionFence");
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-drain", record.ExecutionLease!.OwnerId);
        Assert.Equal(drainLease.ExpiresAt, record.ExecutionLease.ExpiresAt);
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

    // A lease on another run fences nothing here: the committer stamps a fence only from a lease on the commit's run.
    [Fact]
    public async Task ActorCommand_InsideAFlowThatHoldsAnotherRun_TakesItsOwnRunsLease()
    {
        var harness = await LeasedHarness.CreateAsync();
        var other = await harness.OwnershipOf("node-alteration").AcquireAsync("workflow-other");

        using (harness.OwnershipContext.Push(other))
        {
            await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));
            Assert.Same(other, harness.OwnershipContext.Current);
        }

        var fence = Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!;
        Assert.NotEqual(other.LeaseId, fence.LeaseId);
        Assert.Equal(1, fence.FencingToken);
        Assert.Null((await harness.OwnershipRecordAsync()).ExecutionLease);
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

    // Borrowing is an operation of the runtime's own ownership service. With a replacement the alteration runs as it
    // did before this lease existed.
    [Fact]
    public async Task ActorCommand_WithAReplacementOwnershipService_RunsWithoutALease_AsBefore()
    {
        var harness = await LeasedHarness.CreateAsync(replacementOwnership: true);
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Null(Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence);
        var lease = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal(drainLease.LeaseId, lease.LeaseId);
        Assert.Equal(1, lease.FencingToken);
    }

    // The owner is read from the record revision the acquisition was written against: an owner that acquires after
    // the record was first read, and before it is written, is the owner this borrow replaces.
    [Fact]
    public async Task Borrowing_RemembersAnOwnerThatTookTheRunBetweenTheBorrowsReadAndItsWrite()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var drain = new RuntimeExecutionOwnershipService(liveness, new FakeTimeProvider(Now), new RuntimeExecutionOwnershipOptions { OwnerId = "node-drain" });
        // The service reads the clock between reading the record and writing it.
        var clock = new HookedClock(Now, () => drain.AcquireAsync("workflow-1").AsTask().GetAwaiter().GetResult());
        var alteration = new RuntimeExecutionOwnershipService(liveness, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-alteration" });

        var borrowed = await alteration.BorrowAsync("workflow-1");

        Assert.Equal("node-drain", borrowed.OwnersLease!.OwnerId);
        Assert.Equal("node-drain", borrowed.OwnersHeartbeat!.OwnerId);
        Assert.Equal(2, borrowed.Lease.FencingToken);
        Assert.Equal("node-alteration", Assert.Single(await liveness.ListAsync("workflow-1")).ExecutionLease!.OwnerId);
    }

    [Fact]
    public async Task Borrowing_FindsNoOwner_AfterTheOwnerReleased()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drain = harness.OwnershipOf("node-drain");
        await drain.ReleaseAsync(await drain.AcquireAsync("workflow-1"));

        var borrowed = await harness.OwnershipOf("node-alteration").BorrowAsync("workflow-1");

        Assert.Null(borrowed.OwnersLease);
        Assert.Null(borrowed.OwnersHeartbeat);
    }

    // The borrower stopped before it gave the owner's lease back. Its lease is what is left of that owner, so the
    // next borrower treats it as one.
    [Fact]
    public async Task Borrowing_FromABorrowerThatStoodInForAnOwner_FindsAnOwner()
    {
        var harness = await LeasedHarness.CreateAsync();
        await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");
        var stopped = await harness.OwnershipOf("node-stopped").BorrowAsync("workflow-1");
        var next = harness.OwnershipOf("node-alteration");

        var borrowed = await next.BorrowAsync("workflow-1");
        await next.ReturnAsync(borrowed);

        Assert.Equal(stopped.Lease.LeaseId, borrowed.OwnersLease!.LeaseId);
        var lease = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal("node-stopped", lease.OwnerId);
        Assert.True(RuntimeExecutionOwnershipService.StandsInForAnOwner(lease));
    }

    [Fact]
    public async Task Returning_AfterTheRunWasTaken_WritesNothing()
    {
        var harness = await LeasedHarness.CreateAsync();
        await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");
        var alteration = harness.OwnershipOf("node-alteration");
        var borrowed = await alteration.BorrowAsync("workflow-1");
        var taken = await harness.OwnershipOf("node-second-drain").AcquireAsync("workflow-1");

        var result = await alteration.ReturnAsync(borrowed);

        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, result.Status);
        var lease = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal(taken.LeaseId, lease.LeaseId);
        Assert.Equal(taken.FencingToken, lease.FencingToken);
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
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
                // No heartbeat scope: the in-memory store is safe to renew through the foreground service.
                new AlterationRunLease(ownership, OwnershipContext, heartbeatScopes: null, Time));
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
            new(Liveness, Time, new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = LeaseDuration });

        public async Task<ExecutionLivenessState> OwnershipRecordAsync() =>
            Assert.Single(await Liveness.ListAsync("workflow-1"));

        // What the migrate handler asks: the real probe, exempting the lease the caller holds.
        public async Task<WorkflowMigrationQuiescenceReport> ProbeAsOwnerAsync() =>
            await WorkflowMigrationQuiescenceTests.NewProbe(Liveness, timeProvider: Time)
                .ProbeAsync("workflow-1", OwnershipContext.Current!.ToFence());

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
}
