using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Default <see cref="IRuntimeCoalescingDrainScopeFactory"/>. Creates a <see cref="RuntimeCoalescingSession"/> per drain,
/// pushes it onto the ambient <see cref="IRuntimeCoalescingSessionAccessor"/>, and terminally folds a buffered
/// Prepared segment through the durable provider at quiescence.
/// </summary>
public sealed class RuntimeCoalescingDrainScopeFactory(
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
    CoalescingInner<IRuntimePostCommitOutboxStore> innerOutboxStore,
    IRuntimeCheckpointPreparedLedgerStore preparedLedgerStore,
    CoalescingRuntimeCheckpointPersistenceOptions options) : IRuntimeCoalescingDrainScopeFactory
{
    public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, int? maxSegmentCheckpoints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        // A per-workflow authored segment cap (ADR 0032 R5) overrides the host default for this run only; the host
        // options singleton is left untouched. When unspecified (or equal) the shared host options are reused as-is.
        var sessionOptions = maxSegmentCheckpoints is { } cap && cap != options.MaxSegmentCheckpoints
            ? new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = cap }
            : options;

        var session = new RuntimeCoalescingSession(workflowExecutionId, innerQueue.Value, sessionOptions, innerOutboxStore.Value);
        var handle = sessionAccessor.Push(session);
        return new Scope(session, preparedLedgerStore, handle);
    }

    private sealed class Scope(
        RuntimeCoalescingSession session,
        IRuntimeCheckpointPreparedLedgerStore preparedLedgerStore,
        IDisposable scopeHandle) : IRuntimeCoalescingDrainScope
    {
        public RuntimeCoalescingSession Session => session;

        public async ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Already flushed at a boundary (or cap) during the drain: nothing more to coalesce.
            if (!session.IsActive)
                return;

            // Active but empty: the drain consumed queue items without buffering a deferred checkpoint. Reconcile the
            // durable queue with the overlay consumption and end the segment; no terminal fold is required.
            if (!session.HasBufferedChanges && !session.RequiresDurableOutboxReconciliation)
            {
                await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
                session.ClearBuffer();
                session.Deactivate();
                return;
            }

            if (!session.HasBufferedChanges)
            {
                throw new InvalidOperationException(
                    "A durably persisted post-commit outcome cannot be acknowledged without a later durable checkpoint fold.");
            }

            var request = session.CreatePreparedFoldRequest();
            var result = await preparedLedgerStore.CommitPreparedFoldAsync(request, cancellationToken);
            if (result.Status is not (RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay))
            {
                throw new InvalidOperationException(
                    $"Prepared checkpoint fold for workflow execution '{session.WorkflowExecutionId}' failed with '{result.Status}'.");
            }

            session.InvalidateInspectionBaselines();
            var recordedAt = session.BufferedPreparedCommits[^1].Commit.Checkpoint.OccurredAt;
            await session.ReconcileDurablyPersistedOutboxAsync(recordedAt, cancellationToken);
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
            session.ClearBuffer();
            session.Deactivate();
        }

        public ValueTask DisposeAsync()
        {
            scopeHandle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
