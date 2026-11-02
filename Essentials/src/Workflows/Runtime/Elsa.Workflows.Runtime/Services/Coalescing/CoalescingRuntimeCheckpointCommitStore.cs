using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Coalescing decorator for <see cref="IRuntimeCheckpointCommitStore"/>. While a coalescing session owns
/// the target workflow execution, deferred checkpoints are buffered into an in-memory working set and folded into a
/// single atomic durable commit at a flush boundary (attempt activation, suspension/fault/cancellation/completion, an
/// operational, incident, bookmark, alteration-job, child-dispatch or dispatch-cancellation write, the per-segment
/// activity cap of Durable or Balanced, or the end-of-drain quiescence flush).
/// The one boundary that may be dropped instead is the completion of an Ephemeral run that has no record, on a host
/// that turned completion records off (see <see cref="RuntimeCoalescingSession.CanDiscardCompletion"/>). A durable attempt
/// boundary, cap-hit fold-and-flush, nonterminal native suspension, bookmark creation or child dispatch can start
/// another segment in the same drain after its immediate durable commit. Terminal, admission and control boundaries
/// end the session. When no session is active (outside a drain, or once a boundary ended the session), Deferred decisions are normalized to Immediate
/// because there is no working set to flush later; any other decision is forwarded unchanged.
/// </summary>
/// <remarks>
/// This decorator never bypasses single-writer ownership fencing: the folded flush is routed back through
/// <see cref="RuntimeCheckpointCommitter"/> (via the drain scope's quiescence flush), and every boundary commit reaching
/// this store has already been fenced by the committer for the same workflow execution. The durable scheduler queue is
/// advanced only after the folded commit lands (condition B), so a crash mid-segment replays from the last flushed state.
/// </remarks>
public sealed class CoalescingRuntimeCheckpointCommitStore : IRuntimeCheckpointCommitStore
{
    private readonly IRuntimeCoalescingSessionAccessor sessionAccessor;
    private readonly IRuntimeExecutionOwnershipService? ownershipService;

    public CoalescingRuntimeCheckpointCommitStore(CoalescingInner<IRuntimeCheckpointCommitStore> inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor, IRuntimeExecutionOwnershipService? ownershipService = null)
        : this(inner, sessionAccessor, ownershipService, []) { }

    public CoalescingRuntimeCheckpointCommitStore(CoalescingInner<IRuntimeCheckpointCommitStore> inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor, IRuntimeExecutionOwnershipService? ownershipService,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sessionAccessor);
        ArgumentNullException.ThrowIfNull(contributions);
        _inner = inner.Value;
        this.sessionAccessor = sessionAccessor;
        this.ownershipService = ownershipService;
        // Preserve contribution validation; no already-persisted outbox item is restaged by a later fold.
        _ = RuntimePostCommitOutboxItems.RequiredSourceKinds(contributions);
    }

    private static readonly RuntimeCheckpointPersistenceDecision ImmediateDecision =
        new(RuntimeCheckpointPersistenceMode.Immediate, "Coalesced segment flush.");

    private static readonly RuntimeCheckpointPersistenceDecision NoActiveSessionDecision =
        new(RuntimeCheckpointPersistenceMode.Immediate, "No matching active coalescing session.");

    private readonly IRuntimeCheckpointCommitStore _inner;

    public bool SupportsExactBookmarkResume => _inner.SupportsExactBookmarkResume;
    bool IRuntimeCheckpointCommitStore.SupportsModelParentAdmission => _inner.SupportsModelParentAdmission;
    bool IRuntimeCheckpointCommitStore.SupportsModelCheckpoint => _inner.SupportsModelCheckpoint;
    bool IRuntimeCheckpointCommitStore.CanCommitModelState(IRuntimeModelExecutionReader reader) => _inner.CanCommitModelState(reader);
    bool IRuntimeCheckpointCommitStore.SupportsModelAdmission => _inner.SupportsModelAdmission;
    bool IRuntimeCheckpointCommitStore.CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
        IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox) => _inner.CanCommitModelAdmission(reader, executions, activities, outbox);
    public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore physicalExecutions,
        IActivityExecutionStateStore activities) => _inner.CanCommitExactBookmarkResume(bookmarks, physicalExecutions, activities);


    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);

        if (commit.ExpectedModelParentWait is not null)
        {
            if (!_inner.SupportsModelParentAdmission)
                throw new NotSupportedException("The physical writer cannot compare a model child's live native parent wait.");
            commit.EnsureModelParentAdmissionShape();
        }

        if (commit.ExpectedModelExecution is not null)
        {
            if (!_inner.SupportsModelAdmission || decision.Mode != RuntimeCheckpointPersistenceMode.Immediate ||
                sessionAccessor.Current is { HasBufferedChanges: true } admissionSession && admissionSession.AppliesTo(commit.WorkflowExecutionId))
                throw new NotSupportedException("Initial model-owned native worker admission requires its own immediate physical checkpoint.");
            commit.EnsureModelAdmissionShape();
        }

        if (commit.ModelState is not null)
        {
            if (!_inner.SupportsModelCheckpoint || sessionAccessor.Current?.AppliesTo(commit.WorkflowExecutionId) == true)
                throw new NotSupportedException("Model decisions require a qualified physical writer outside a native coalescing session.");
            if (decision.Mode != RuntimeCheckpointPersistenceMode.Immediate)
                throw new NotSupportedException("Model decisions cannot be deferred or skipped.");
            commit.EnsureModelStateShape();
            return await _inner.CommitAsync(commit, decision, cancellationToken);
        }

        if (commit.ExpectedBookmarkResume is not null && !SupportsExactBookmarkResume)
            throw new NotSupportedException("The physical checkpoint store cannot enforce exact bookmark resume admission.");

        // Capture the ambient owner before the await so a concurrent context change cannot redirect this write lease to
        // another session. Even a non-matching commit fences the current owner's raw-page memo conservatively.
        var session = sessionAccessor.Current;
        session?.EnsurePreparedFlushAllows(commit);
        if (session is null || !session.AppliesTo(commit.WorkflowExecutionId))
        {
            // Deferred has meaning only inside this matching working set. In particular, a legacy queue's disabled
            // scope must not ask a third-party store to defer a write that no active session will later flush.
            var directDecision = decision.Mode == RuntimeCheckpointPersistenceMode.Deferred ? NoActiveSessionDecision : decision;
            return await CommitInnerAsync(session, commit, directDecision, cancellationToken);
        }

        // The synthetic folded quiescence-flush commit built by the drain scope: it already represents the whole
        // segment (routed here through the committer so fencing gates it). Apply straight to inner and end the segment.
        if (commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush))
        {
            var flushResult = await CommitInnerAsync(session, commit, decision, cancellationToken);
            try
            {
                await session.ReconcileDurablyPersistedOutboxAsync(commit.Checkpoint.OccurredAt, cancellationToken);
                await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
            }
            catch
            {
                AbandonAfterDurableCommit(session);
                throw;
            }

            session.ClearBuffer();
            session.Deactivate();
            return flushResult;
        }

        var forceImmediate = HasBoundaryState(commit);
        var deferrable = decision.Mode == RuntimeCheckpointPersistenceMode.Deferred && !forceImmediate;
        var capReached = !IsAttemptClaim(commit) && session.WouldExceedActivityCap(commit);

        // Deferrable, non-boundary, under cap: buffer into the overlay working set and acknowledge this commit's own
        // outbox items so the committer's post-commit count assertion is satisfied without durably persisting anything.
        if (deferrable && !capReached)
        {
            session.BufferDeferred(commit);
            return OwnOutbox(commit);
        }

        // A deferrable commit that trips the per-segment hop cap folds-and-flushes like a mandatory boundary but keeps
        // the session active: the flush empties the buffer, so a fresh segment starts and a long replayable hot loop
        // keeps coalescing at one durable commit per cap-sized window instead of degrading to per-checkpoint writes.
        var capFold = deferrable && capReached;

        // Flush boundary (or cap): fold the buffered segment with this commit into one atomic durable write, then
        // advance the durable queue. Qualified nonterminal boundaries start a fresh segment so later replayable work
        // in the same drain can coalesce while every suspension, dispatch and before-user-code boundary stays durable.
        var remainingPendingOutbox = session.RemainingPendingOutboxChanges();

        // The one completion that is not committed: an Ephemeral run, on a host that opted out of completion records,
        // that was never committed and leaves nothing behind that outlives it. Nothing reaches the durable store, but
        // the queued work the run consumed is still removed from it, so the finished run is not started again.
        if (session.CanDiscardCompletion(commit, remainingPendingOutbox))
        {
            try
            {
                // Every other path proves this node still owns the run when the durable store checks the commit's
                // fence. Nothing reaches the store here, so check the fence before touching the durable queue: a node
                // that lost the run must not remove the work its new owner is about to run.
                if (commit.ExpectedFence is { } fence && ownershipService is not null)
                    await ownershipService.EnsureCurrentAsync(commit.WorkflowExecutionId, fence.FencingToken, cancellationToken);
                await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
            }
            finally
            {
                session.ClearBuffer();
                session.Deactivate();
            }

            return OwnOutbox(commit);
        }

        var continueAfterBoundary = capFold || CanContinueAfterBoundary(commit);
        if (session.HasBufferedChanges)
        {
            var foldedState = session.FoldBufferedStateChangesWith(commit.StateChanges);
            var outbox = UnionOutbox(FoldableOutbox(session, remainingPendingOutbox), commit.StateChanges.PostCommitOutbox);
            var foldedCommit = commit with
            {
                StateChanges = foldedState.WithPostCommitOutbox(outbox),
                PostCommitIntents = [],
            };

            // Each buffered commit and this trailing one were validated by the committer, but a fold of valid commits is
            // a new commit and can combine changes no single commit carried. It is the one commit this decorator builds
            // itself, so it is validated here before the durable store sees it; every other path forwards the
            // committer's already-validated commit unchanged.
            RuntimeCheckpointCommitValidator.Validate(foldedCommit);
            if (continueAfterBoundary)
                await session.EnsureActiveFusedScheduleAnchorsAsync(cancellationToken);
            await CommitInnerAsync(session, foldedCommit, ImmediateDecision, cancellationToken);
            try
            {
                if (continueAfterBoundary)
                {
                    // Scheduler continuations stay overlay-delivered with a durable crash backstop. External intents
                    // use durable claims immediately; remember all persisted IDs so later folds cannot restage them.
                    session.RecordCapFlushState(commit.StateChanges);
                    session.MarkOutboxDurablyPersisted(outbox);
                }
                await session.ReconcileDurablyPersistedOutboxAsync(commit.Checkpoint.OccurredAt, cancellationToken);
                // A deactivating boundary — and a cap fold, whose trailing commit rode the in-flight dispatch — already
                // carries the in-flight item's effect in this flush, so the item must be consumed here instead of
                // surviving as a durable duplicate (see AdvanceInnerQueueAsync). A retaining checkpoint or
                // pre-activation attempt boundary keeps it for redrive.
                await session.AdvanceInnerQueueAsync(
                    consumeInFlightClaims: !commit.RetainSchedulerWork && (!continueAfterBoundary || !IsAttemptClaim(commit)),
                    cancellationToken,
                    preserveActiveFusedScheduleAnchors: continueAfterBoundary);
            }
            catch
            {
                AbandonAfterDurableCommit(session);
                throw;
            }

            session.ClearBuffer();
            if (!continueAfterBoundary)
                session.Deactivate();
            return OwnOutbox(commit);
        }

        // Nothing buffered: pass this commit straight through, then reconcile the durable queue with any overlay
        // consumption that happened before the first checkpoint.
        if (continueAfterBoundary)
            await session.EnsureActiveFusedScheduleAnchorsAsync(cancellationToken);
        var passthrough = await CommitInnerAsync(session, commit, decision, cancellationToken);
        try
        {
            if (continueAfterBoundary)
            {
                session.RecordCapFlushState(commit.StateChanges);
                session.MarkOutboxDurablyPersisted(commit.StateChanges.PostCommitOutbox);
            }
            await session.ReconcileDurablyPersistedOutboxAsync(commit.Checkpoint.OccurredAt, cancellationToken);
            await session.AdvanceInnerQueueAsync(
                consumeInFlightClaims: !commit.RetainSchedulerWork && (!continueAfterBoundary || !IsAttemptClaim(commit)),
                cancellationToken,
                preserveActiveFusedScheduleAnchors: continueAfterBoundary);
        }
        catch
        {
            AbandonAfterDurableCommit(session);
            throw;
        }

        session.ClearBuffer();
        if (!continueAfterBoundary)
            session.Deactivate();
        return passthrough;
    }

    // The inner commit has landed, so everything the session buffered is durable whatever the reconciliation after it
    // does. When that reconciliation fails, the drain records the failure against the in-flight item and carries on to
    // its quiescence flush (#2601). Ending the segment here keeps that flush from re-applying the now older buffered
    // state over the newer durable state, and stops the overlay from standing in for a durable queue it no longer tracks.
    private static void AbandonAfterDurableCommit(RuntimeCoalescingSession session)
    {
        session.ClearBuffer();
        session.Deactivate();
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitInnerAsync(
        RuntimeCoalescingSession? session,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        RuntimeCheckpointCommitStoreResult result;
        if (session?.DurableValuePageMemo is not { } memo)
        {
            result = await _inner.CommitAsync(commit, decision, cancellationToken);
        }
        else
        {
            using var write = memo.BeginWrite();
            result = await _inner.CommitAsync(commit, decision, cancellationToken);
            write.Succeed();
        }

        session?.RecordCommittedPumpedEffect(commit);
        // From here the run has a durable record, so its completion is always committed.
        if (session is not null && StringComparer.Ordinal.Equals(session.WorkflowExecutionId, commit.WorkflowExecutionId))
            session.MarkDurableRecord();
        return result;
    }

    // A checkpoint's outbox carries only Pending items, but an overlay continuation whose delivery failed retryably is
    // FailedRetryable until it is retried (EnqueueSchedulerWork retries, #2225). It is folded as the Pending crash backstop
    // it is, keeping its attempt count and retry time so the sweep that delivers it counts the attempt already made. One
    // an earlier flush already persisted is left out: its durable Pending row is that backstop, and the overlay's changed
    // copy would be a conflicting second save of the same item. Both are what the quiescence flush does with such an item.
    private static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> FoldableOutbox(
        RuntimeCoalescingSession session,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> remaining) =>
        remaining
            .Where(change => !session.IsOutboxDurablyPersisted(change.StateId))
            .Select(change => change.State.Status == RuntimePostCommitOutboxStatus.FailedRetryable
                ? change with { State = AsPending(change.State) }
                : change)
            .ToArray();

    private static RuntimePostCommitOutboxItem AsPending(RuntimePostCommitOutboxItem item) =>
        new(
            item.OutboxItemId,
            item.Intent,
            RuntimePostCommitOutboxStatus.Pending,
            item.RecordedAt,
            item.AvailableAt,
            item.RetryPolicy,
            item.DeliveryAttemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: null,
            lastFailureMessage: item.LastFailureMessage,
            metadata: item.Metadata,
            deliveryFencingToken: item.DeliveryFencingToken,
            deliveryVisibleAfter: null,
            stimulusDeliveryProtocolVersion: item.StimulusDeliveryProtocolVersion,
            stimulusDeliverySourceScope: item.StimulusDeliverySourceScope,
            recipientSnapshot: item.RecipientSnapshot);

    // A claim is taken just before the claimed work runs, and the work item that runs it can exist only in the session's
    // memory: a parent re-claimed to evaluate its finished child runs from such an item. A cap fold at the claim would
    // commit the claim but nothing that runs the work again, so a crash before the queue is advanced left the parent
    // Running with nothing queued. The cap therefore waits for the next checkpoint, which records what the work did.
    private static bool IsAttemptClaim(RuntimeCheckpointCommit commit) =>
        StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed);

    private static bool CanContinueAfterBoundary(RuntimeCheckpointCommit commit)
    {
        if (commit.ModelState is not null || commit.ExpectedModelExecution is not null ||
            commit.ExpectedModelParentWait is not null || commit.ExpectedBookmarkResume is not null ||
            commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Delete } ||
            commit.StateChanges.WorkflowExecution?.State.Status.IsTerminal() == true ||
            commit.Checkpoint.Name is RuntimeCheckpointNames.WorkflowCompleted or RuntimeCheckpointNames.WorkflowFaulted or RuntimeCheckpointNames.WorkflowCancelled ||
            commit.StateChanges.Operational.Count > 0 || commit.StateChanges.Incidents.Count > 0 ||
            !commit.StateChanges.BackgroundActivityWork.IsEmpty || commit.StateChanges.AlterationJobTerminalChange is not null ||
            commit.StateChanges.WorkflowDispatchCancellations.Count > 0)
            return false;

        return commit.Checkpoint.Name is RuntimeCheckpointNames.ActivityAttemptClaimed or RuntimeCheckpointNames.ActivitySuspended or
                   RuntimeCheckpointNames.BookmarkCreated or RuntimeCheckpointNames.WorkflowSuspended ||
               commit.StateChanges.WorkflowDispatches.Count > 0;
    }

    // Operational (lease/heartbeat/ownership — condition E), incident, and bookmark writes must never be buffered:
    // they are durability-critical and are flushed immediately even if they ride on a deferrable checkpoint name.
    private static bool HasBoundaryState(RuntimeCheckpointCommit commit) =>
        commit.ExpectedModelExecution is not null ||
        commit.ExpectedBookmarkResume is not null ||
        commit.StateChanges.Operational.Count > 0 ||
        commit.StateChanges.Incidents.Count > 0 ||
        commit.StateChanges.Bookmarks.Count > 0 ||
        !commit.StateChanges.BackgroundActivityWork.IsEmpty ||
        commit.StateChanges.AlterationJobTerminalChange is not null ||
        commit.StateChanges.WorkflowDispatches.Count > 0 ||
        commit.StateChanges.WorkflowDispatchCancellations.Count > 0;

    private static RuntimeCheckpointCommitStoreResult OwnOutbox(RuntimeCheckpointCommit commit) =>
        new(commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray());

    private static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> UnionOutbox(
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> remaining,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> own)
    {
        var merged = new Dictionary<string, RuntimeStateChange<RuntimePostCommitOutboxItem>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var change in remaining.Concat(own))
        {
            if (merged.TryAdd(change.StateId, change))
                order.Add(change.StateId);
            else
                merged[change.StateId] = change;
        }

        return order.Select(id => merged[id]).ToArray();
    }
}
