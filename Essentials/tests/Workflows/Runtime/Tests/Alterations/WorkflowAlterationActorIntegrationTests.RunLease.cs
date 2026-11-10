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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

// #2667: an alteration reads its run and commits under the run's ownership lease, so its checkpoint is fenced. It
// acquires the run as a drain does, but it borrows the lease. When the run has an owner, the borrowed lease stands in
// for that owner: the owner's heartbeat stays on the record, and afterwards the lease stays too, with the expiry of
// the owner's lease.
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

    // An alteration during a held lease. It takes the run as a drain would and the owner it took the run from is
    // refused, but the ownership record keeps saying that the owner was there: its heartbeat stays, and the lease
    // that is left expires when the owner's would have. Nobody holds that lease, the alteration included.
    [Fact]
    public async Task ActorCommand_OnARunAnotherOwnerHolds_TakesItOver_AndLeavesTheRecordOfThatOwner()
    {
        WorkflowMigrationQuiescenceReport? quiescence = null;
        ExecutionLivenessState? duringTheBorrow = null;
        IReadOnlyCollection<ExecutionLivenessState>? listedUnderTheOwner = null;
        IReadOnlyCollection<ExecutionLivenessState>? listedUnderTheBorrower = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ =>
        {
            quiescence = await harness!.ProbeAsOwnerAsync();
            duringTheBorrow = await harness.OwnershipRecordAsync();
            // The query host reclaim runs for a departed host id.
            listedUnderTheOwner = (await harness.Liveness.ListOwnedPageAsync("node-drain", new RuntimeStorePageRequest(10, null))).Items;
            listedUnderTheBorrower = (await harness.Liveness.ListOwnedPageAsync("node-alteration", new RuntimeStorePageRequest(10, null))).Items;
        }));
        var drain = harness.OwnershipOf("node-drain");
        var drainLease = await drain.AcquireAsync("workflow-1");
        var drainHeartbeat = (await harness.OwnershipRecordAsync()).Heartbeat!;
        harness.Time.Advance(TimeSpan.FromSeconds(5));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        // While it ran: its own lease, in its own name, beside the owner's heartbeat. Either host's departure lists the run.
        Assert.Equal("node-alteration", duringTheBorrow!.ExecutionLease!.OwnerId);
        Assert.Equal(harness.Time.GetUtcNow(), duringTheBorrow.ExecutionLease.AcquiredAt);
        AssertSameHeartbeat(drainHeartbeat, duringTheBorrow.Heartbeat);
        Assert.Equal("workflow-1", Assert.Single(listedUnderTheOwner!).WorkflowExecutionId);
        Assert.Equal("workflow-1", Assert.Single(listedUnderTheBorrower!).WorkflowExecutionId);
        var fence = Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence!;
        Assert.Equal(duringTheBorrow.ExecutionLease.LeaseId, fence.LeaseId);
        Assert.Equal(2, fence.FencingToken);
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

        // Afterwards: the lease is still the alteration's, acquired when it borrowed, but it expires when the
        // owner's would have, under a token nobody holds, and the owner's heartbeat is untouched. Recovery reads the
        // expiry, host reclaim the heartbeat.
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-alteration", record.ExecutionLease!.OwnerId);
        Assert.Equal(duringTheBorrow.ExecutionLease.AcquiredAt, record.ExecutionLease.AcquiredAt);
        Assert.Equal(drainLease.ExpiresAt, record.ExecutionLease.ExpiresAt);
        Assert.Equal(3, record.ExecutionLease.FencingToken);
        Assert.True(BorrowedLeaseMark.StandsInForAnOwner(record.ExecutionLease));
        AssertSameHeartbeat(drainHeartbeat, record.Heartbeat);
        Assert.Equal(
            RuntimeExecutionOwnershipTransitionStatus.Stale,
            (await harness.OwnershipOf("node-alteration").HeartbeatAsync(duringTheBorrow.ExecutionLease)).Status);
    }

    // An expired lease is how recovery finds a run whose owner stopped mid-drain: the run is a recovery candidate now.
    // While the alteration runs, its live lease puts that off: if it stopped there, recovery would come one lease
    // duration after its last renewal. Once it is done the run is a candidate at once again, and host reclaim still
    // finds it under the owner.
    [Fact]
    public async Task ActorCommand_OnARunWhoseOwnerStopped_Runs_AndTheRunIsARecoveryCandidateAgainAtOnce()
    {
        ExecutionLivenessState? duringTheBorrow = null;
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(new CompletingHandler(async _ => duringTheBorrow = await harness!.OwnershipRecordAsync()));
        await harness.OwnershipOf("node-dead").AcquireAsync("workflow-1");
        harness.Time.Advance(TimeSpan.FromMinutes(2));
        var now = harness.Time.GetUtcNow();
        var sweep = new RuntimeRecoveryScanRequest(now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), limit: 10);
        Assert.Single(RuntimeRecoveryCandidateSelector.Select([await harness.OwnershipRecordAsync()], sweep));

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        Assert.Empty(RuntimeRecoveryCandidateSelector.Select([duringTheBorrow!], sweep));
        Assert.Equal(now + LeaseDuration, RuntimeRecoveryCandidateSelector.GetEligibleAt(duringTheBorrow!, new RuntimeRecoveryScanRequest(now + LeaseDuration, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), limit: 10)));
        // The lease that is left expired one tick after it was acquired, which is the moment the alteration borrowed.
        var record = await harness.OwnershipRecordAsync();
        var aTickLater = new RuntimeRecoveryScanRequest(now.AddTicks(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), limit: 10);
        Assert.True(record.ExecutionLease!.IsExpired(now.AddTicks(1)));
        Assert.Equal(RuntimeInterruptionReason.LeaseLost, Assert.Single(RuntimeRecoveryCandidateSelector.Select([record], aTickLater)).Reason);
        Assert.Equal("workflow-1", Assert.Single((await harness.Liveness.ListOwnedPageAsync("node-dead", new RuntimeStorePageRequest(10, null))).Items).WorkflowExecutionId);
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
        Assert.Null((await harness.OwnershipRecordAsync()).Heartbeat);
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

    // A renewal rewrites the lease and, for a drain, the heartbeat. Under a lease that stands in for an owner it must
    // keep the mark and the owner's heartbeat. The owner's lease is three lease durations old here, so a renewal
    // paced by the lease's age instead of its duration would come too late.
    [Fact]
    public async Task ActorCommand_ThatOutlastsARenewal_StillStandsInForTheOwner_AndKeepsItsHeartbeat()
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
        var drainHeartbeat = (await harness.OwnershipRecordAsync()).Heartbeat!;
        harness.Time.Advance(LeaseDuration * 3);

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Contains(quiescence!.Findings, finding => finding.Code == "ActiveExecutionFence");
        var record = await harness.OwnershipRecordAsync();
        Assert.True(BorrowedLeaseMark.StandsInForAnOwner(record.ExecutionLease!));
        // The owner's lease expired long before the borrow, so what is left is expired from the moment it was acquired.
        Assert.True(drainLease.ExpiresAt < record.ExecutionLease!.AcquiredAt);
        Assert.Equal(record.ExecutionLease.AcquiredAt.AddTicks(1), record.ExecutionLease.ExpiresAt);
        AssertSameHeartbeat(drainHeartbeat, record.Heartbeat);
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

    // The alteration committed under its fence. Losing the run before the lease is returned is not a failure of the
    // job, whether the return would have released the lease or left it standing in for an owner.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActorCommand_ThatLosesTheRunAfterItCommitted_Succeeds(bool runHadAnOwner)
    {
        LeasedHarness? harness = null;
        harness = await LeasedHarness.CreateAsync(afterCommit: async () =>
            await harness!.OwnershipOf("node-drain").AcquireAsync("workflow-1"));
        if (runHadAnOwner)
            await harness.OwnershipOf("node-first-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await harness.Alterations.FindJobAsync(harness.Claim.JobId))!.Status);
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-drain", record.ExecutionLease!.OwnerId);
        Assert.Equal("node-drain", record.Heartbeat!.OwnerId);
        Assert.Contains(harness.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("was lost or could not be returned"));
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
    // did before this lease existed, and says so.
    [Fact]
    public async Task ActorCommand_WithAReplacementOwnershipService_RunsWithoutALease_AndSaysSo()
    {
        var harness = await LeasedHarness.CreateAsync(replacementOwnership: true);
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");

        await harness.Executor.ExecuteAsync(NewEnvelope(harness.Claim));

        Assert.Null(Assert.Single(harness.Checkpoints.ListCommits()).Commit.ExpectedFence);
        var lease = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal(drainLease.LeaseId, lease.LeaseId);
        Assert.Equal(1, lease.FencingToken);
        // A warning the first time in a process and debug after that, so the level depends on test order.
        var entry = Assert.Single(harness.Log.Entries);
        Assert.Contains(entry.Level, new[] { LogLevel.Warning, LogLevel.Debug });
        Assert.Contains("runs without the run's ownership lease", entry.Message);
        Assert.Contains(nameof(ReplacementOwnershipService), entry.Message);
    }

    // The owner is read from the record revision the acquisition was written against: an owner that acquires after
    // the record was first read, and before it is written, is the owner this borrow stands in for.
    [Fact]
    public async Task Borrowing_StandsInForAnOwnerThatTookTheRunBetweenTheBorrowsReadAndItsWrite()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var drain = new RuntimeExecutionOwnershipService(liveness, new FakeTimeProvider(Now), new RuntimeExecutionOwnershipOptions { OwnerId = "node-drain" });
        RuntimeExecutionLease? drainLease = null;
        // The service reads the clock between reading the record and writing it.
        var clock = new HookedClock(Now, () => drainLease = drain.AcquireAsync("workflow-1").AsTask().GetAwaiter().GetResult());
        var alteration = new RuntimeExecutionOwnershipService(liveness, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-alteration" });

        var borrow = await alteration.BorrowAsync("workflow-1");

        Assert.Equal(drainLease!.ExpiresAt, borrow.OwnersLeaseExpiresAt);
        Assert.Equal(2, borrow.Lease.FencingToken);
        var stored = Assert.Single(await liveness.ListAsync("workflow-1"));
        Assert.Equal(borrow.Lease.LeaseId, stored.ExecutionLease!.LeaseId);
        Assert.True(BorrowedLeaseMark.StandsInForAnOwner(stored.ExecutionLease));
        Assert.Equal("node-drain", stored.Heartbeat!.OwnerId);
    }

    [Fact]
    public async Task Borrowing_FindsNoOwner_AfterTheOwnerReleased()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drain = harness.OwnershipOf("node-drain");
        await drain.ReleaseAsync(await drain.AcquireAsync("workflow-1"));

        var borrow = await harness.OwnershipOf("node-alteration").BorrowAsync("workflow-1");

        Assert.Null(borrow.OwnersLeaseExpiresAt);
        Assert.True(BorrowedLeaseMark.IsFromNoOwner(borrow.Lease));
        Assert.Equal("node-alteration", (await harness.OwnershipRecordAsync()).Heartbeat!.OwnerId);
    }

    // Two alterations on two nodes overlap on a run that has an owner, or the first one stops before it returns its
    // lease. The second finds the first one's lease, which carries the expiry of the owner's lease, and the owner's
    // heartbeat beside it. What it leaves expires when what the first would have left expires.
    [Fact]
    public async Task Borrowing_FromABorrowerThatStandsInForAnOwner_LeavesTheSameRecordOfThatOwner()
    {
        var harness = await LeasedHarness.CreateAsync();
        var drainLease = await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");
        var drainHeartbeat = (await harness.OwnershipRecordAsync()).Heartbeat!;
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        var first = harness.OwnershipOf("node-first");
        var firstBorrow = await first.BorrowAsync("workflow-1");
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        var second = harness.OwnershipOf("node-second");

        var secondBorrow = await second.BorrowAsync("workflow-1");
        var returned = await second.ReturnAsync(secondBorrow);
        var returnedByTheFirst = await first.ReturnAsync(firstBorrow);

        Assert.Equal(drainLease.ExpiresAt, secondBorrow.OwnersLeaseExpiresAt);
        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, returned.Status);
        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, returnedByTheFirst.Status);
        var record = await harness.OwnershipRecordAsync();
        Assert.Equal("node-second", record.ExecutionLease!.OwnerId);
        Assert.Equal(secondBorrow.Lease.AcquiredAt, record.ExecutionLease.AcquiredAt);
        Assert.Equal(drainLease.ExpiresAt, record.ExecutionLease.ExpiresAt);
        Assert.Equal(4, record.ExecutionLease.FencingToken);
        Assert.True(BorrowedLeaseMark.StandsInForAnOwner(record.ExecutionLease));
        AssertSameHeartbeat(drainHeartbeat, record.Heartbeat);
    }

    // A mark that cannot be read must not block the run: the stand-in's own expiry is used instead.
    [Fact]
    public async Task Borrowing_FromAStandInWhoseMarkIsDamaged_UsesThatLeasesOwnExpiry()
    {
        var harness = await LeasedHarness.CreateAsync();
        await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");
        var first = await harness.OwnershipOf("node-first").BorrowAsync("workflow-1");
        var record = await harness.OwnershipRecordAsync();
        var damaged = new RuntimeExecutionLease(
            first.Lease.LeaseId, "workflow-1", first.Lease.OwnerId, first.Lease.AcquiredAt, first.Lease.ExpiresAt, first.Lease.FencingToken,
            first.Lease.Metadata.ToDictionary(entry => entry.Key, entry => entry.Key.EndsWith("ExpiresAt", StringComparison.Ordinal) ? "not-a-time" : entry.Value));
        await harness.Liveness.SaveAsync(new ExecutionLivenessState(
            record.ExecutionLivenessStateId, "workflow-1", damaged, record.Heartbeat, record.Drain, record.InterruptedExecution, record.PendingPostCommitIntentIds, record.Metadata));

        var second = await harness.OwnershipOf("node-second").BorrowAsync("workflow-1");

        Assert.NotEqual(first.Lease.Metadata, damaged.Metadata);
        Assert.Equal(first.Lease.ExpiresAt, second.OwnersLeaseExpiresAt);
    }

    [Fact]
    public async Task Returning_AfterTheRunWasTaken_WritesNothing()
    {
        var harness = await LeasedHarness.CreateAsync();
        await harness.OwnershipOf("node-drain").AcquireAsync("workflow-1");
        var alteration = harness.OwnershipOf("node-alteration");
        var borrow = await alteration.BorrowAsync("workflow-1");
        var taken = await harness.OwnershipOf("node-second-drain").AcquireAsync("workflow-1");

        var result = await alteration.ReturnAsync(borrow);

        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, result.Status);
        var lease = (await harness.OwnershipRecordAsync()).ExecutionLease!;
        Assert.Equal(taken.LeaseId, lease.LeaseId);
        Assert.Equal(taken.FencingToken, lease.FencingToken);
    }

    [Fact]
    public void RuntimeCore_ComposesAnExecutorThatRunsItsJobsUnderTheLease()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = Assert.IsType<WorkflowAlterationActorCommandExecutor>(
            scope.ServiceProvider.GetRequiredService<IWorkflowAlterationActorCommandExecutor>());

        Assert.True(executor.RunsJobsUnderLease);
    }

    // The drain orchestrator's rule: on scoped persistence a renewal needs an operation scope of its own.
    [Fact]
    public void LeaseRunner_OnScopedPersistence_RequiresAHeartbeatScope()
    {
        var scopedStore = System.Reflection.DispatchProxy.Create<IExecutionLivenessStateStore, UnusedLivenessStore>();
        var ownership = new RuntimeExecutionOwnershipService(scopedStore);

        Assert.Throws<InvalidOperationException>(() =>
            new AlterationLeaseRunner(ownership, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), heartbeatScopes: null, TimeProvider.System));
    }

    // Any liveness store other than the runtime's in-memory one. Nothing in the test calls it.
    public class UnusedLivenessStore : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }

    private static void AssertSameHeartbeat(RuntimeHeartbeat expected, RuntimeHeartbeat? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.HeartbeatId, actual.HeartbeatId);
        Assert.Equal(expected.OwnerId, actual.OwnerId);
        Assert.Equal(expected.LeaseId, actual.LeaseId);
        Assert.Equal(expected.RecordedAt, actual.RecordedAt);
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
                new AlterationLeaseRunner(ownership, OwnershipContext, heartbeatScopes: null, Time, Log));
        }

        public FakeTimeProvider Time { get; } = new(Now);
        public RecordingLogger Log { get; } = new();
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

        // What a handler that offers the lease it holds for exemption asks of the real probe.
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

    private sealed class RecordingLogger : ILogger<AlterationLeaseRunner>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
