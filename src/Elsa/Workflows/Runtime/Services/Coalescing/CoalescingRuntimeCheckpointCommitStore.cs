using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing decorator for <see cref="IRuntimeCheckpointCommitStore"/> (E3-6, RT-10). While a coalescing session owns
/// the target workflow execution, deferrable checkpoints may be buffered into an in-memory working set. Terminal
/// Prepared folding finalizes a buffered segment through the durable provider's one exact atomic operation. When no
/// session is active this decorator passes through to the durable inner store, so the default path is unaffected.
/// </summary>
public sealed class CoalescingRuntimeCheckpointCommitStore(
    CoalescingInner<IRuntimeCheckpointCommitStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IRuntimeCheckpointCommitStore, IRuntimeCheckpointPreparedLedgerStore
{
    private readonly IRuntimeCheckpointCommitStore _inner = inner.Value;
    private readonly IRuntimeCheckpointPreparedLedgerStore _preparedLedger = inner.Value as IRuntimeCheckpointPreparedLedgerStore
        ?? throw new InvalidOperationException(
            $"Checkpoint coalescing requires the selected durable provider to implement {nameof(IRuntimeCheckpointPreparedLedgerStore)}.");

    public async ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (sessionAccessor.Current is { } session && session.AppliesTo(request.Commit.WorkflowExecutionId))
        {
            if (request.RecoveryAuthority is { } recoveryAuthority)
                await session.MaterializeOverlayRecoverySourceAsync(recoveryAuthority, cancellationToken);

            // Keep the current overlay claim active through its checkpoint commit. Otherwise the committer would try
            // to consume an overlay-issued claim from the durable queue it never claimed. The immediate boundary below
            // is the handoff point that retires this session after the commit succeeds.

            return await _preparedLedger.PrepareAsync(
                request with
                {
                    InitialPersistenceMode = session.RequiresDurableRecoveryHandoff
                        ? RuntimeCheckpointPersistenceMode.Immediate
                        : RuntimeCheckpointPersistenceMode.Deferred
                },
                cancellationToken);
        }

        return await _preparedLedger.PrepareAsync(request, cancellationToken);
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);

        if (sessionAccessor.Current is not { } session || !session.AppliesTo(commit.WorkflowExecutionId))
            return await _preparedLedger.CommitPreparedAsync(token, commit, decision, cancellationToken);

        var reservation = await FindPreparedReservationAsync(commit.WorkflowExecutionId, token.CommitId, cancellationToken);
        if (reservation is null)
        {
            var replay = await _preparedLedger.CommitPreparedAsync(token, commit, decision, cancellationToken);
            if (replay.Status == RuntimeCheckpointCommitStoreStatus.Replay)
            {
                // A prior successful boundary has already made its state/outbox durable, but this live session may
                // still own its overlay frontier. Retire that frontier exactly as an ordinary completed boundary;
                // do not re-import the continuation or repeat any durable effect.
                session.InvalidateInspectionBaselines();
                await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
                session.ClearBuffer();
                session.Deactivate();
            }

            return replay;
        }
        var preparedCommit = new RuntimeCheckpointPreparedCommit(token, commit, decision)
        {
            CurrentAuthorityFence = reservation.CurrentAuthorityFence,
            AuthorityRevision = reservation.AuthorityRevision
        };

        var deferrable = decision.Mode == RuntimeCheckpointPersistenceMode.Deferred && !HasBoundaryState(commit);
        var capReached = session.HopCount + 1 > session.MaxSegmentCheckpoints;
        if (deferrable && !capReached)
        {
            session.BufferDeferred(preparedCommit);
            return OwnOutbox(commit);
        }

        var remainingPendingOutbox = session.RemainingPendingOutboxChanges();
        RuntimeCheckpointCommitStoreResult passthrough;
        RuntimeCheckpointPreparedFoldRequest? foldRequest = null;
        if (session.BufferedPreparedCommits.Count > 0)
        {
            foldRequest = session.CreatePreparedFoldRequest(preparedCommit);
            var foldResult = await _preparedLedger.CommitPreparedFoldAsync(foldRequest, cancellationToken);
            passthrough = foldResult.Receipts.TryGetValue(token.CommitId, out var receipt)
                ? receipt
                : new RuntimeCheckpointCommitStoreResult([]) { Status = foldResult.Status };
        }
        else
        {
            passthrough = await _preparedLedger.CommitPreparedAsync(token, commit, decision, cancellationToken);
        }

        // Preserve the buffered segment and its queue/outbox ownership when durable finalization did not succeed.
        // The committer will surface Conflict/OwnershipLost to its caller, which may then retry or recover safely.
        if (passthrough.Status is not (RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay))
            return passthrough;

        var continuationOutbox = commit.StateChanges.PostCommitOutbox;
        var qualifyingSchedulerContinuation = passthrough.Status == RuntimeCheckpointCommitStoreStatus.Committed &&
                                               QualifiesAsSchedulerContinuation(preparedCommit, continuationOutbox);
        var continueAfterBoundary = !session.RequiresDurableRecoveryHandoff &&
                                    (capReached ||
                                    qualifyingSchedulerContinuation ||
                                    CanContinueAfterBoundary(commit, remainingPendingOutbox.Count));

        session.InvalidateInspectionBaselines();
        if (capReached)
        {
            session.RecordCapFlushState(commit.StateChanges);
            session.MarkOutboxDurablyPersisted(
                foldRequest?.FoldedStateChanges.PostCommitOutbox ?? commit.StateChanges.PostCommitOutbox);
        }
        else if (qualifyingSchedulerContinuation)
        {
            // This boundary already committed the exact Pending rows durably. Keep that durable row as the crash
            // backstop while importing the same row into the existing single-writer overlay for the D2 pump; a
            // later successful fold is the first point at which its Delivered outcome can be reconciled durably.
            session.RecordDurableSchedulerContinuationState(commit.StateChanges);
            session.MarkOutboxDurablyPersisted(continuationOutbox);
        }
        else if (continueAfterBoundary)
            session.RecordDurableBoundaryState(commit.StateChanges);
        await session.ReconcileDurablyPersistedOutboxAsync(commit.Checkpoint.OccurredAt, cancellationToken);
        await session.AdvanceInnerQueueAsync(
            consumeInFlightClaims: capReached || qualifyingSchedulerContinuation || !continueAfterBoundary,
            cancellationToken);
        session.ClearBuffer();
        if (!continueAfterBoundary)
            session.Deactivate();
        return passthrough;
    }

    public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default) =>
        _preparedLedger.PagePreparedAsync(query, cancellationToken);

    public ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default) =>
        _preparedLedger.AdoptPreparedAsync(request, cancellationToken);

    public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default)
        => _preparedLedger.CommitPreparedFoldAsync(request, cancellationToken);

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);

        if (sessionAccessor.Current is not { } session || !session.AppliesTo(commit.WorkflowExecutionId))
            return await _inner.CommitAsync(commit, decision, cancellationToken);

        throw new InvalidOperationException(
            "An active coalescing session requires durable prepared-checkpoint finalization; the legacy commit path is disabled.");
    }

    // Operational (lease/heartbeat/ownership — condition E), incident, and bookmark writes must never be buffered:
    // they are durability-critical and are flushed immediately even if they ride on a deferrable checkpoint name.
    private static bool HasBoundaryState(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.Operational.Count > 0 ||
        commit.StateChanges.Incidents.Count > 0 ||
        commit.StateChanges.Bookmarks.Count > 0 ||
        commit.StateChanges.AlterationJobTerminalChange is not null ||
        commit.StateChanges.WorkflowDispatches.Count > 0 ||
        commit.StateChanges.WorkflowDispatchCancellations.Count > 0;

    private static bool CanContinueAfterBoundary(
        RuntimeCheckpointCommit commit,
        int pendingSegmentOutboxCount) =>
        pendingSegmentOutboxCount == 0 &&
        commit.StateChanges.PostCommitOutbox.Count == 0 &&
        StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed);

    private static bool QualifiesAsSchedulerContinuation(
        RuntimeCheckpointPreparedCommit preparedCommit,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> committedOutbox) =>
        preparedCommit.Decision.Mode == RuntimeCheckpointPersistenceMode.Immediate &&
        preparedCommit.Token.Provenance.ExecutionContext is { IsEmpty: true } &&
        committedOutbox.Count > 0 &&
        committedOutbox.All(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State.Status == RuntimePostCommitOutboxStatus.Pending &&
            StringComparer.Ordinal.Equals(change.State.Intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

    private static RuntimeCheckpointCommitStoreResult OwnOutbox(RuntimeCheckpointCommit commit) =>
        new(commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray());

    private async ValueTask<RuntimeCheckpointPreparedReservation?> FindPreparedReservationAsync(
        string workflowExecutionId,
        string commitId,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            var page = await _preparedLedger.PagePreparedAsync(
                new RuntimeCheckpointPreparedQuery(
                    workflowExecutionId,
                    RuntimeCheckpointPreparedQuery.MaximumPageSize,
                    cursor),
                cancellationToken);
            var reservation = page.Reservations.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.CommitId, commitId));
            if (reservation is not null)
                return reservation;
            cursor = page.NextCursor;
        } while (cursor is not null);

        return null;
    }

}
