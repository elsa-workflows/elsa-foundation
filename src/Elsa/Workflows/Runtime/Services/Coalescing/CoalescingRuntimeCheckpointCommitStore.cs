using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing decorator for <see cref="IRuntimeCheckpointCommitStore"/> (E3-6, RT-10). While a coalescing session owns
/// the target workflow execution, deferrable checkpoints may be buffered into an in-memory working set. Terminal
/// Prepared folding is a separately reviewed work unit, so any boundary, cap, or quiescence path that would require
/// it fails closed before mutating durable state. When no session is active this decorator passes through to the
/// durable inner store, so the default (Immediate) path is unaffected.
/// </summary>
/// <remarks>
/// This stage deliberately exposes no executable terminal-fold path. Durable Prepared reservations remain untouched
/// when the stage gate is reached.
/// </remarks>
public sealed class CoalescingRuntimeCheckpointCommitStore(
    CoalescingInner<IRuntimeCheckpointCommitStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IRuntimeCheckpointCommitStore, IRuntimeCheckpointPreparedLedgerStore
{
    private readonly IRuntimeCheckpointCommitStore _inner = inner.Value;
    private readonly IRuntimeCheckpointPreparedLedgerStore _preparedLedger = inner.Value as IRuntimeCheckpointPreparedLedgerStore
        ?? throw new InvalidOperationException(
            $"Checkpoint coalescing requires the selected durable provider to implement {nameof(IRuntimeCheckpointPreparedLedgerStore)}.");

    public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var candidateMode = sessionAccessor.Current is { } session && session.AppliesTo(request.Commit.WorkflowExecutionId)
            ? RuntimeCheckpointPersistenceMode.Deferred
            : request.InitialPersistenceMode;
        return _preparedLedger.PrepareAsync(request with { InitialPersistenceMode = candidateMode }, cancellationToken);
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

        var deferrable = decision.Mode == RuntimeCheckpointPersistenceMode.Deferred && !HasBoundaryState(commit);
        var capReached = session.HopCount + 1 > session.MaxSegmentCheckpoints;
        if (deferrable && !capReached)
        {
            session.BufferDeferred(new RuntimeCheckpointPreparedCommit(token, commit, decision));
            return OwnOutbox(commit);
        }

        var remainingPendingOutbox = session.RemainingPendingOutboxChanges();
        var continueAfterBoundary = capReached || CanContinueAfterBoundary(commit, remainingPendingOutbox.Count);
        if (session.BufferedPreparedCommits.Count > 0)
            throw new NotSupportedException(
                "Prepared checkpoint terminal folding is not enabled until the reviewed adoption/fold work unit is approved.");

        var passthrough = await _preparedLedger.CommitPreparedAsync(token, commit, decision, cancellationToken);
        session.InvalidateInspectionBaselines();
        if (continueAfterBoundary)
            session.RecordDurableBoundaryState(commit.StateChanges);
        await session.ReconcileDurablyPersistedOutboxAsync(commit.Checkpoint.OccurredAt, cancellationToken);
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: !continueAfterBoundary, cancellationToken);
        session.ClearBuffer();
        if (!continueAfterBoundary)
            session.Deactivate();
        return passthrough;
    }

    public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default) =>
        _preparedLedger.PagePreparedAsync(query, cancellationToken);

    public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Prepared checkpoint terminal folding is not enabled until the reviewed adoption/fold work unit is approved.");
    }

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

    private static RuntimeCheckpointCommitStoreResult OwnOutbox(RuntimeCheckpointCommit commit) =>
        new(commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray());

}
