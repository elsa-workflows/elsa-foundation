using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// WU-1 / spec 105: the scheduler work-item acknowledgement is folded into the checkpoint commit's unit-of-work, so a
/// claim-capable drain step performs one durable transaction and skips the separate <c>CompleteClaimAsync</c>.
/// </summary>
public sealed class RuntimeDrainStepSingleTransactionTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private readonly DateTimeOffset _now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    // (a) The claimed work item is deleted through the checkpoint commit when the handler commits, and the drainer does
    // NOT issue a separate CompleteClaimAsync (single durable transaction).
    [Fact]
    public async Task DrainStep_WhenHandlerCommits_ConsumesInsideCommitAndSkipsSeparateAck()
    {
        var queue = new CountingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = NewStore(queue);
        var committer = NewCommitter(store, accessor);
        var drainer = NewDrainer(queue, [new CommittingHandler(committer)], accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(1, result.DrainedCount);
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Equal(1, queue.ConsumeClaimedCalls); // acked inside the commit
        Assert.Equal(0, queue.CompleteClaimCalls);  // no second durable transaction
        Assert.Single(store.ListCommits());
    }

    // (a) When the handler produces no commit, the item is NOT consumed atomically, so the drainer falls back to the
    // legacy CompleteClaimAsync — demonstrating the "iff the commit lands" contract from the other side.
    [Fact]
    public async Task DrainStep_WhenHandlerDoesNotCommit_FallsBackToLegacyAck()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new CountingWorkQueue(innerQueue);
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var drainer = NewDrainer(queue, [new NoCommitHandler()], accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(1, result.DrainedCount);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Equal(1, queue.CompleteClaimCalls); // legacy ack
    }

    // (b) A stale claimant's commit fails claim-lost and persists nothing; the successor-owned item survives.
    [Fact]
    public async Task Commit_WhenClaimReclaimedBySuccessor_FailsClaimLostAndPersistsNothing()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = NewStore(queue);
        var committer = NewCommitter(store, accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        // First claimant (fencing token 1).
        var staleClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowExecutionId, "owner-a", _now, TimeSpan.FromSeconds(30)));
        Assert.NotNull(staleClaim);
        // A successor reclaims after the visibility window (fencing token advances to 2).
        var successorClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowExecutionId, "owner-b", _now.AddMinutes(1), TimeSpan.FromSeconds(30)));
        Assert.NotNull(successorClaim);
        Assert.NotEqual(staleClaim!.FencingToken, successorClaim!.FencingToken);

        using (accessor.Begin(ConsumedSchedulerWorkItem.FromClaim(staleClaim)))
        {
            await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(
                () => committer.CommitAsync(NewCommit("commit-stale")).AsTask());
            Assert.False(accessor.WasConsumedDurably);
        }

        Assert.Empty(store.ListCommits()); // nothing persisted
        var survivors = await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId));
        Assert.Single(survivors.Items); // successor-owned item survives
    }

    // (b) A renewed claim (owner + fencing token unchanged, only revision advanced) still consumes successfully.
    [Fact]
    public async Task Commit_AfterRenewal_StillConsumesBecauseFenceIsOwnerAndToken()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = NewStore(queue);
        var committer = NewCommitter(store, accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowExecutionId, "owner-a", _now, TimeSpan.FromSeconds(30)));
        Assert.NotNull(claim);
        // Renewal advances the revision but keeps owner + fencing token.
        var renewed = await queue.RenewClaimAsync(claim!, _now.AddSeconds(5), TimeSpan.FromSeconds(30));
        Assert.True(renewed.Succeeded);

        // The consume record captured at claim time (revision 1) must still succeed after the renewal (revision 2).
        using (accessor.Begin(ConsumedSchedulerWorkItem.FromClaim(claim!)))
        {
            await committer.CommitAsync(NewCommit("commit-renewed"));
            Assert.True(accessor.WasConsumedDurably);
        }

        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
    }

    // (c) A handler that faults before committing still ack-deletes via the legacy fault path and poisons exactly once.
    [Fact]
    public async Task DrainStep_WhenHandlerFaultsBeforeCommit_LegacyAcksAndPoisonsOnce()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, [new FaultingHandler()], accessor, poisonStore);
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.False(accessor.WasConsumedDurably);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items); // ack-deleted
        var poison = await poisonStore.FindAsync(WorkflowExecutionId, "work-1");
        Assert.NotNull(poison);
        Assert.Equal(1, poison!.FailureCount); // poisoned exactly once
    }

    // QA defect fix: a handler whose FIRST checkpoint commit lands (and durably consumes the item) but which THEN throws
    // must surface a Faulted result with poison recorded exactly once — not a spurious claim-lost from the fault path
    // trying to CompleteClaimAsync an already-consumed claim.
    [Fact]
    public async Task DrainStep_WhenHandlerFaultsAfterConsumingCommit_FaultsAndPoisonsWithoutClaimLost()
    {
        var queue = new CountingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = NewStore(queue);
        var committer = NewCommitter(store, accessor);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, [new CommitThenThrowHandler(committer)], accessor, poisonStore);
        await queue.EnqueueAsync(NewWorkItem(1));

        // No RuntimeSchedulerWorkClaimLostException may escape: the drain must complete with a Faulted item result.
        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items); // consumed by the commit
        Assert.Equal(1, queue.ConsumeClaimedCalls);
        Assert.Equal(0, queue.CompleteClaimCalls); // no second ack attempted against the consumed claim
        var poison = await poisonStore.FindAsync(WorkflowExecutionId, "work-1");
        Assert.NotNull(poison);
        Assert.Equal(1, poison!.FailureCount); // poisoned exactly once (#412 bounded delivery)
    }

    // QA property pin: the consumed-claim scope resets per dispatch. Item A's dispatch consumes durably; item B's dispatch
    // produces no commit and must therefore take the legacy CompleteClaimAsync path. If WasConsumedDurably leaked from A
    // to B, B would skip its ack and its item would redeliver forever after the visibility timeout.
    [Fact]
    public async Task DrainSteps_ConsumedFlagDoesNotLeakAcrossSequentialDispatches()
    {
        var queue = new CountingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = NewStore(queue);
        var committer = NewCommitter(store, accessor);
        // work-1 → committing handler (atomic consume); work-2 → no-commit handler (must legacy-ack).
        var drainer = NewDrainer(
            queue,
            [new FirstItemOnlyCommittingHandler(committer), new NoCommitHandler()],
            accessor);
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(2, result.DrainedCount);
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        Assert.Equal(1, queue.ConsumeClaimedCalls);  // item A: consumed inside its commit
        Assert.Equal(1, queue.CompleteClaimCalls);   // item B: legacy ack — the flag did not leak
    }

    // (d) Coalesced mode: consumed work items fold correctly across a segment (union, no loss or duplication).
    [Fact]
    public void Fold_UnionsConsumedSchedulerWorkItemsAcrossSegment()
    {
        var first = EmptyChangeSet().WithConsumedSchedulerWorkItems(
            [new ConsumedSchedulerWorkItem(WorkflowExecutionId, "work-1", "owner-a", 1)]);
        var second = EmptyChangeSet().WithConsumedSchedulerWorkItems(
        [
            new ConsumedSchedulerWorkItem(WorkflowExecutionId, "work-2", "owner-a", 1),
            // A repeat of work-1 across the segment must collapse to a single entry.
            new ConsumedSchedulerWorkItem(WorkflowExecutionId, "work-1", "owner-a", 1)
        ]);

        var folded = RuntimeCheckpointFold.Fold([first, second]);

        var ids = folded.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal("work-1", ids[0]);
        Assert.Equal("work-2", ids[1]);
    }

    // --- Wiring helpers ---------------------------------------------------------------------------------------------

    private static RuntimeCheckpointStateChangeSet EmptyChangeSet() =>
        new(null, null, [], [], [], [], []);

    private RuntimeCheckpointCommit NewCommit(string commitId) =>
        new(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{commitId}",
                Name: RuntimeCheckpointNames.PostCommitIntentRecorded,
                WorkflowExecutionId: WorkflowExecutionId,
                OccurredAt: _now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: EmptyChangeSet(),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static InMemoryRuntimeCheckpointCommitStore NewStore(IWorkflowSchedulerWorkQueue queue) =>
        new(schedulerWorkQueue: queue);

    private static RuntimeCheckpointCommitter NewCommitter(
        IRuntimeCheckpointCommitStore store,
        IRuntimeConsumedSchedulerWorkClaimAccessor accessor) =>
        new(
            new ImmediatePolicy(),
            store,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [],
            intentHandlerContributions: [],
            consumedWorkClaimAccessor: accessor,
            coalescingSessionAccessor: null);

    private WorkflowSchedulerDrainer NewDrainer(
        IWorkflowSchedulerWorkQueue queue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        IRuntimeConsumedSchedulerWorkClaimAccessor accessor,
        IWorkflowSchedulerPoisonStore? poisonStore = null) =>
        new(
            queue,
            handlers,
            new InMemoryWorkflowExecutionStateStore(),
            new FixedTimeProvider(_now),
            pauseGate: null,
            pipelineDispatcher: null,
            faultCapturePolicy: null,
            poisonStore: poisonStore,
            retryPolicy: null,
            tracer: null,
            claimOptions: null,
            consumedWorkClaimAccessor: accessor);

    private RuntimeSchedulerWorkItem NewWorkItem(int index) =>
        new(
            workItemId: $"work-{index}",
            workflowExecutionId: WorkflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{WorkflowExecutionId}:command-{index}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: null,
            commandMetadata: null);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ImmediatePolicy : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            new(new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate, "test"));
    }

    private static RuntimeCheckpointCommit NewHandlerCommit(RuntimeSchedulerWorkItem workItem) =>
        new(
            CommitId: $"commit:{workItem.WorkItemId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workItem.WorkItemId}",
                Name: RuntimeCheckpointNames.PostCommitIntentRecorded,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: workItem.RecordedAt,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: EmptyChangeSet(),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private sealed class CommittingHandler(RuntimeCheckpointCommitter committer) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(CommittingHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            await committer.CommitAsync(NewHandlerCommit(workItem), cancellationToken);
    }

    /// <summary>Commits a checkpoint (which durably consumes the claimed item), then throws — the fault-after-commit case.</summary>
    private sealed class CommitThenThrowHandler(RuntimeCheckpointCommitter committer) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(CommitThenThrowHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            await committer.CommitAsync(NewHandlerCommit(workItem), cancellationToken);
            throw new InvalidOperationException("boom after commit");
        }
    }

    /// <summary>Handles only work-1 (with a consuming commit) so a later no-commit handler takes work-2.</summary>
    private sealed class FirstItemOnlyCommittingHandler(RuntimeCheckpointCommitter committer) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FirstItemOnlyCommittingHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => StringComparer.Ordinal.Equals(workItem.WorkItemId, "work-1");

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            await committer.CommitAsync(NewHandlerCommit(workItem), cancellationToken);
    }

    private sealed class NoCommitHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(NoCommitHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FaultingHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FaultingHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    /// <summary>Counting decorator over a claim-capable queue to observe which acknowledgement path ran.</summary>
    private sealed class CountingWorkQueue(IWorkflowSchedulerWorkQueue inner) : IWorkflowSchedulerWorkQueue
    {
        public int ConsumeClaimedCalls { get; private set; }
        public int CompleteClaimCalls { get; private set; }

        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            inner.EnqueueAsync(workItem, cancellationToken);

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default)
        {
            CompleteClaimCalls++;
            return inner.CompleteClaimAsync(claim, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
            inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default)
        {
            ConsumeClaimedCalls++;
            return inner.ConsumeClaimedAsync(consumed, cancellationToken);
        }
    }
}
