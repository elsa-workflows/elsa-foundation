using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #4032: a drain that lost its run keeps running until a write of its own is refused. A handler that throws in that
/// time must not have its step acknowledged, and must not leave a poison record on a run another owner runs. The
/// drainer checks the run's ownership before it writes anything of the fault, and then writes the record under its
/// lease where the poison store can check that lease atomically.
/// </summary>
public sealed class WorkflowSchedulerFencedPoisonDrainTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("taken-over", RuntimeFencingRejectionReason.StaleToken)]
    [InlineData("expired", RuntimeFencingRejectionReason.ExpiredLease)]
    [InlineData("borrowed", RuntimeFencingRejectionReason.StaleToken)]
    public async Task DrainAsync_HandlerFaultAfterTheDrainLostItsRun_ThrowsStaleFenceAndLeavesTheStepQueuedWithNoRecord(
        string lostBecause,
        RuntimeFencingRejectionReason expectedReason)
    {
        // Another node took the run, the lease ran out with nobody else there, or an alteration borrowed it.
        var fixture = NewFixture();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        var retryPolicy = new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now"));
        var drainer = fixture.NewDrainer(store, retryPolicy, () => fixture.LoseRunAsync(lostBecause));
        await fixture.Queue.EnqueueAsync(NewWorkItem());
        fixture.Queue.ResetCounts();

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(expectedReason, refusal.Reason);
        // Nothing of the fault arm was written: no acknowledgement, no RetryNow re-enqueue, no release, no record.
        Assert.Equal((0, 0, 0), (fixture.Queue.Completions, fixture.Queue.Enqueues, fixture.Queue.Releases));
        Assert.Equal("work-1", Assert.Single(await fixture.Queue.Inner.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).WorkItemId);
        Assert.Empty(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal((0, 0), (store.FencedRecords, store.UnfencedRecords));
        // The refusal comes first: it costs one ownership read, and neither the poison-store read nor a retry decision.
        Assert.Equal((1, 0, 0), (fixture.Ownership.EnsureCurrentCalls, store.Finds, retryPolicy.Decisions));
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWhileTheDrainOwnsTheRun_RecordsPoisonUnderItsFence()
    {
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal(1, record.FailureCount);
        Assert.Equal((1, 0), (store.FencedRecords, store.UnfencedRecords));
        Assert.Equal(1, fixture.Ownership.EnsureCurrentCalls);
        Assert.Empty(await fixture.Queue.Inner.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task DrainAsync_HandlerThatSucceedsUnderALease_ReadsNoOwnershipAndAsksThePoisonStoreNothing()
    {
        // The ownership check and the capability lookup belong to the fault path only.
        var fixture = NewFixture();
        var store = new ThrowingCapabilityPoisonStore();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store, faults: false);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.False(result.StoppedOnFault);
        Assert.Equal(0, fixture.Ownership.EnsureCurrentCalls);
        Assert.Equal(0, store.CapabilityReads);
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWithNoAmbientLease_RecordsPoisonUnfencedWithoutAnOwnershipCheck()
    {
        // Nothing pushed a lease: a drainer driven outside the drain orchestrator, as before this change. Another
        // node owns the run, so a check or a fenced record would refuse; the step is poisoned anyway.
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        await fixture.NodeB.AcquireAsync(WorkflowExecutionId);
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal((0, 1), (store.FencedRecords, store.UnfencedRecords));
        Assert.Equal(0, fixture.Ownership.EnsureCurrentCalls);
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultUnderALeaseOfAnotherExecution_RecordsPoisonUnfencedWithoutAnOwnershipCheck()
    {
        // The checkpoint committer attaches a lease only to commits of the lease's own execution. The fault path
        // follows the same rule, so a lease of another run neither checks nor fences this one. Another node owns
        // this run under a later token, so a check with the other run's lease would refuse.
        var fixture = NewFixture();
        var store = new CountingFencedPoisonStore(fixture.FencedPoisonStore);
        await fixture.NodeB.AcquireAsync(WorkflowExecutionId);
        await fixture.NodeB.AcquireAsync(WorkflowExecutionId);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync("wfexec-other"));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Single(await store.ListAsync(WorkflowExecutionId));
        Assert.Equal((0, 1), (store.FencedRecords, store.UnfencedRecords));
        Assert.Equal(0, fixture.Ownership.EnsureCurrentCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrainAsync_HandlerFaultWhenThePoisonStoreCannotCheckTheFence_RecordsPoisonUnfenced(bool hasCapability)
    {
        // A decorator that does not forward the capability, and a store whose ownership state lives in another
        // backend, both leave the record unfenced. The drain owns the run when it checks, and loses it while the
        // step is acknowledged; the record is written all the same.
        var fixture = NewFixture();
        var inner = new InMemoryWorkflowSchedulerPoisonStore();
        IWorkflowSchedulerPoisonStore store = hasCapability ? new CountingFencedPoisonStore(inner) : new PlainPoisonStore(inner);
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        fixture.Queue.OnCompleteClaim = () => fixture.LoseRunAsync("taken-over");
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.False(inner.CanRecordFenced);
        Assert.Single(await inner.ListAsync(WorkflowExecutionId));
        if (store is CountingFencedPoisonStore counting)
            Assert.Equal((0, 1), (counting.FencedRecords, counting.UnfencedRecords));
    }

    [Fact]
    public async Task DrainAsync_WhenThePoisonStoreCannotSayWhetherItRecordsFenced_ThrowsBeforeTheStepIsAcknowledged()
    {
        // Asking a store whether it can record fenced runs its lookup of the ownership store, which can throw. That
        // must happen before the acknowledgement, or the step would be gone with nothing recorded.
        var fixture = NewFixture();
        var store = new ThrowingCapabilityPoisonStore();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());
        fixture.Queue.ResetCounts();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(ThrowingCapabilityPoisonStore.Message, failure.Message);
        Assert.Equal((0, 0, 0), (fixture.Queue.Completions, fixture.Queue.Enqueues, fixture.Queue.Releases));
        Assert.Single(await fixture.Queue.Inner.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
        Assert.Equal(0, store.Records);
    }

    [Theory]
    [InlineData("acknowledged")]
    [InlineData("retry-now")]
    [InlineData("retry-after")]
    public async Task DrainAsync_WhenTheRunIsLostBetweenTheCheckAndTheRecord_LeavesTheQueueChangeAndNoRecord(string queueChange)
    {
        // The drain owned the run when it checked, and lost it before the record. A store that can check the lease
        // refuses the record. The acknowledgement, the re-enqueue for RetryNow and the release for an honored
        // RetryAfter are not undone, as a process crash at that point would leave them.
        var fixture = NewFixture();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        IRuntimeDomainRetryPolicy? retryPolicy = queueChange switch
        {
            "retry-now" => new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")),
            "retry-after" => new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, TimeSpan.FromMinutes(5), "retry-after")),
            _ => null
        };
        var drainer = fixture.NewDrainer(fixture.FencedPoisonStore, retryPolicy, hasResumptionRedriver: true);
        fixture.Queue.OnCompleteClaim = () => fixture.LoseRunAsync("taken-over");
        fixture.Queue.OnReleaseClaim = () => fixture.LoseRunAsync("taken-over");
        // A step kind whose claim can be released with a delay.
        await fixture.Queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity));
        fixture.Queue.ResetCounts();

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        var expected = queueChange switch
        {
            "retry-now" => (Completions: 1, Enqueues: 1, Releases: 0, Queued: 1),
            "retry-after" => (Completions: 0, Enqueues: 0, Releases: 1, Queued: 1),
            _ => (Completions: 1, Enqueues: 0, Releases: 0, Queued: 0)
        };
        var queued = await fixture.Queue.Inner.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId));
        Assert.Equal(expected, (fixture.Queue.Completions, fixture.Queue.Enqueues, fixture.Queue.Releases, queued.Count));
        Assert.Empty(await fixture.FencedPoisonStore.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task DrainAsync_WhenTheFencedRecordFailsForAReasonOtherThanOwnership_RecordsUnfencedAndReturnsTheFault()
    {
        // The drain owned the run when it checked. A fenced write that then fails for another reason, here a write
        // conflict the store gave up on, must not leave the step acknowledged with no record: the record goes the way
        // it goes for a store without the capability.
        var fixture = NewFixture();
        var store = new FailingFencedPoisonStore(() => new InvalidOperationException("The fenced write did not settle."));
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());
        fixture.Queue.ResetCounts();

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, Assert.Single(result.Items).Status);
        Assert.Equal((1, 1), (store.FencedAttempts, store.UnfencedRecords));
        Assert.Equal("work-1", Assert.Single(await store.ListAsync(WorkflowExecutionId)).WorkItemId);
        Assert.Equal(1, fixture.Queue.Completions);
        Assert.Empty(await fixture.Queue.Inner.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task DrainAsync_WhenTheFencedRecordIsCancelled_LetsTheCancellationThroughWithoutAnUnfencedRecord()
    {
        // Cancellation is not a failed write to work around: writing the record another way would ignore it.
        var fixture = NewFixture();
        var store = new FailingFencedPoisonStore(() => new OperationCanceledException("The fenced write was cancelled."));
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal((1, 0), (store.FencedAttempts, store.UnfencedRecords));
        Assert.Empty(await store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task DrainAsync_WhenTheFencedRecordIsRefusedAsStale_WritesNoUnfencedRecord()
    {
        // Only a lost lease refuses the record, and nothing may write it another way then.
        var fixture = NewFixture();
        var store = new FailingFencedPoisonStore(() => new RuntimeStaleFencingTokenException(WorkflowExecutionId, 1, 2));
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(store);
        await fixture.Queue.EnqueueAsync(NewWorkItem());

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal((1, 0), (store.FencedAttempts, store.UnfencedRecords));
        Assert.Empty(await store.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWithALeaseButNoOwnershipService_SkipsTheCheckAndStillFencesTheRecord()
    {
        // A drainer built without the ownership service cannot ask before it acknowledges. The record is still
        // written under the lease, so a drain that lost its run acknowledges the step and records nothing.
        var fixture = NewFixture();
        using var scope = fixture.Accessor.Push(await fixture.NodeA.AcquireAsync(WorkflowExecutionId));
        var drainer = fixture.NewDrainer(fixture.FencedPoisonStore, beforeFault: () => fixture.LoseRunAsync("taken-over"), withOwnershipService: false);
        await fixture.Queue.EnqueueAsync(NewWorkItem());
        fixture.Queue.ResetCounts();

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(1, fixture.Queue.Completions);
        Assert.Equal(0, fixture.Ownership.EnsureCurrentCalls);
        Assert.Empty(await fixture.FencedPoisonStore.ListAsync(WorkflowExecutionId));
    }

    private Fixture NewFixture() => new(new FakeTimeProvider(_now));

    private RuntimeSchedulerWorkItem NewWorkItem(WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.RunSchedulerWork)
    {
        using var document = JsonDocument.Parse("""{"workItemId":"work-1"}""");
        return new(
            workItemId: "work-1",
            workflowExecutionId: WorkflowExecutionId,
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: $"{WorkflowExecutionId}:command-1:{Guid.NewGuid():N}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 1,
            payload: document.RootElement.Clone());
    }

    private sealed class Fixture
    {
        private readonly FakeTimeProvider _clock;

        public Fixture(FakeTimeProvider clock)
        {
            _clock = clock;
            var ownership = new InMemoryExecutionLivenessStateStore();
            NodeA = new RuntimeExecutionOwnershipService(ownership, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-a" });
            NodeB = new RuntimeExecutionOwnershipService(ownership, clock, new RuntimeExecutionOwnershipOptions { OwnerId = "node-b" });
            Ownership = new CountingOwnershipService(NodeA);
            FencedPoisonStore = new InMemoryWorkflowSchedulerPoisonStore(ownership, clock);
        }

        public RecordingWorkQueue Queue { get; } = new(new InMemoryWorkflowSchedulerWorkQueue());
        public AsyncLocalRuntimeExecutionOwnershipContextAccessor Accessor { get; } = new();
        public RuntimeExecutionOwnershipService NodeA { get; }
        public RuntimeExecutionOwnershipService NodeB { get; }
        public CountingOwnershipService Ownership { get; }
        public InMemoryWorkflowSchedulerPoisonStore FencedPoisonStore { get; }

        public async ValueTask LoseRunAsync(string lostBecause)
        {
            switch (lostBecause)
            {
                case "taken-over":
                    await NodeB.AcquireAsync(WorkflowExecutionId);
                    break;
                case "expired":
                    _clock.Advance(TimeSpan.FromMinutes(2));
                    break;
                case "borrowed":
                    await NodeB.BorrowAsync(WorkflowExecutionId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lostBecause));
            }
        }

        public WorkflowSchedulerDrainer NewDrainer(
            IWorkflowSchedulerPoisonStore poisonStore,
            IRuntimeDomainRetryPolicy? retryPolicy = null,
            Func<ValueTask>? beforeFault = null,
            bool withOwnershipService = true,
            bool faults = true,
            bool hasResumptionRedriver = false) =>
            TestSchedulerDrainer.Create(
                Queue,
                [new TestSchedulerWorkHandler(beforeFault, faults), new NoopWorkflowSchedulerWorkHandler()],
                _clock,
                poisonStore: poisonStore,
                retryPolicy: retryPolicy ?? new NoopRuntimeDomainRetryPolicy(),
                hasResumptionRedriver: hasResumptionRedriver,
                ownershipContextAccessor: Accessor,
                ownershipService: withOwnershipService ? Ownership : null);
    }

    private sealed class TestSchedulerWorkHandler(Func<ValueTask>? beforeFault, bool faults) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(TestSchedulerWorkHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (!faults)
                return;
            if (beforeFault is not null)
                await beforeFault();
            throw new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
        }
    }

    private sealed class StubRetryPolicy(RuntimeDomainRetryDecision decision) : IRuntimeDomainRetryPolicy
    {
        public int Decisions { get; private set; }

        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request)
        {
            Decisions++;
            return decision;
        }
    }

    // Counts the ownership reads the drainer makes.
    private sealed class CountingOwnershipService(IRuntimeExecutionOwnershipService inner) : IRuntimeExecutionOwnershipService
    {
        public int EnsureCurrentCalls { get; private set; }

        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(workflowExecutionId, cancellationToken);
        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(lease, cancellationToken);
        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default)
        {
            EnsureCurrentCalls++;
            return inner.EnsureCurrentAsync(workflowExecutionId, fencingToken, cancellationToken);
        }
    }

    // Counts the queue writes of the fault arm, and can lose the run while the step is being acknowledged.
    private sealed class RecordingWorkQueue(InMemoryWorkflowSchedulerWorkQueue queue) : IWorkflowSchedulerWorkQueue
    {
        private readonly IWorkflowSchedulerWorkQueue inner = queue;
        public InMemoryWorkflowSchedulerWorkQueue Inner => queue;
        public Func<ValueTask>? OnCompleteClaim { get; set; }
        public Func<ValueTask>? OnReleaseClaim { get; set; }
        public int Completions { get; private set; }
        public int Enqueues { get; private set; }
        public int Releases { get; private set; }

        public void ResetCounts() => (Completions, Enqueues, Releases) = (0, 0, 0);

        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
        public bool SupportsClaimValidation => inner.SupportsClaimValidation;
        public bool SupportsTargetedDeletion => inner.SupportsTargetedDeletion;
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => inner.DequeueAsync(workflowExecutionId, cancellationToken);
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) => inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) => inner.ClaimAsync(request, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) => inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default) => inner.ConsumeClaimedAsync(consumed, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            Enqueues++;
            return inner.EnqueueAsync(workItem, cancellationToken);
        }

        public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default)
        {
            Completions++;
            var result = await inner.CompleteClaimAsync(claim, cancellationToken);
            if (OnCompleteClaim is not null)
                await OnCompleteClaim();
            return result;
        }

        public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default)
        {
            Releases++;
            var result = await inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
            if (OnReleaseClaim is not null)
                await OnReleaseClaim();
            return result;
        }
    }

    // A decorator that does not implement the capability, as one written before it existed.
    private sealed class PlainPoisonStore(IWorkflowSchedulerPoisonStore inner) : IWorkflowSchedulerPoisonStore
    {
        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default) =>
            inner.RecordAsync(record, cancellationToken);
        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(workflowExecutionId, cancellationToken);
    }

    // A decorator that forwards the capability and counts the calls the drainer made.
    private sealed class CountingFencedPoisonStore(InMemoryWorkflowSchedulerPoisonStore inner) : IWorkflowSchedulerPoisonStore, ISchedulerPoisonFenceCapability
    {
        public int FencedRecords { get; private set; }
        public int UnfencedRecords { get; private set; }
        public int Finds { get; private set; }
        public bool CanRecordFenced => inner.CanRecordFenced;

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(RuntimeSchedulerPoisonRecord record, RuntimeExecutionFence fence, CancellationToken cancellationToken = default)
        {
            FencedRecords++;
            return inner.RecordFencedAsync(record, fence, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
        {
            UnfencedRecords++;
            return inner.RecordAsync(record, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
        {
            Finds++;
            return inner.FindAsync(workflowExecutionId, workItemId, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(workflowExecutionId, cancellationToken);
    }

    // A store that says it can record fenced and whose fenced write then fails with the given exception.
    private sealed class FailingFencedPoisonStore(Func<Exception> failure) : IWorkflowSchedulerPoisonStore, ISchedulerPoisonFenceCapability
    {
        private readonly InMemoryWorkflowSchedulerPoisonStore _inner = new();
        public int FencedAttempts { get; private set; }
        public int UnfencedRecords { get; private set; }
        public bool CanRecordFenced => true;

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(RuntimeSchedulerPoisonRecord record, RuntimeExecutionFence fence, CancellationToken cancellationToken = default)
        {
            FencedAttempts++;
            return ValueTask.FromException<RuntimeSchedulerPoisonRecord>(failure());
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
        {
            UnfencedRecords++;
            return _inner.RecordAsync(record, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(workflowExecutionId, cancellationToken);
    }

    // A store whose lookup of the ownership store fails, so it cannot say whether it records fenced.
    private sealed class ThrowingCapabilityPoisonStore : IWorkflowSchedulerPoisonStore, ISchedulerPoisonFenceCapability
    {
        public const string Message = "The ownership store cannot be resolved.";
        public int CapabilityReads { get; private set; }
        public int Records { get; private set; }

        public bool CanRecordFenced
        {
            get
            {
                CapabilityReads++;
                throw new InvalidOperationException(Message);
            }
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(RuntimeSchedulerPoisonRecord record, RuntimeExecutionFence fence, CancellationToken cancellationToken = default)
        {
            Records++;
            return ValueTask.FromResult(record);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
        {
            Records++;
            return ValueTask.FromResult(record);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeSchedulerPoisonRecord?>(null);
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>>([]);
    }
}
