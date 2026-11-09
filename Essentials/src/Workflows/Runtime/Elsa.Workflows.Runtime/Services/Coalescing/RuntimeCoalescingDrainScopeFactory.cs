using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Default <see cref="IRuntimeCoalescingDrainScopeFactory"/>. Creates a <see cref="RuntimeCoalescingSession"/> per drain,
/// pushes it onto the ambient <see cref="IRuntimeCoalescingSessionAccessor"/>, and flushes the folded segment through
/// the <see cref="RuntimeCheckpointCommitter"/> at quiescence so single-writer ownership fencing gates the single durable write.
/// </summary>
public sealed class RuntimeCoalescingDrainScopeFactory(
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    RuntimeCheckpointCommitter checkpointCommitter,
    CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
    CoalescingInner<IRuntimePostCommitOutboxStore> innerOutboxStore,
    PersistenceModeOptions options,
    TimeProvider timeProvider) : IRuntimeCoalescingDrainScopeFactory
{
    public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(mode);

        // A nested owner gets a fresh memo. The parent cannot resume reusing its old baseline after the child exits.
        sessionAccessor.Current?.DisableDurableValuePageReuse();

        var session = new RuntimeCoalescingSession(workflowExecutionId, innerQueue.Value, options, innerOutboxStore.Value, mode, timeProvider);
        // A legacy provider may not support identity deletion. Never discover that after a folded commit has landed:
        // disable buffering before the drain begins, leaving queue operations and checkpoint writes durable.
        if (!innerQueue.Value.SupportsTargetedDeletion)
            session.Deactivate();
        var handle = sessionAccessor.Push(session);
        return new Scope(session, handle, checkpointCommitter, timeProvider);
    }

    private sealed class Scope(
        RuntimeCoalescingSession session,
        IDisposable scopeHandle,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider) : IRuntimeCoalescingDrainScope
    {
        private RuntimeCheckpointCommit? preparedFlush;
        public RuntimeCoalescingSession Session => session;

        public async ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default)
        {
            // Already flushed at a boundary (or cap) during the drain: nothing more to coalesce.
            if (!session.IsActive)
                return;

            // Active but empty: the drain consumed queue items without buffering any deferred checkpoint. Reconcile the
            // durable queue with the overlay consumption and end the segment; no folded commit is required — unless an
            // earlier cap flush left durably persisted outbox items whose overlay outcome still has to be written back,
            // in which case the (empty-state) flush commit below carries that reconciliation.
            if (preparedFlush is null && !session.HasBufferedChanges && !session.RequiresDurableOutboxReconciliation)
            {
                await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, cancellationToken);
                session.ClearBuffer();
                session.Deactivate();
                return;
            }

            // An uncertain outcome must retry exactly the same identity/timestamp/envelope. A fresh synthetic commit
            // would mint another canonical source after a peer may already have delivered the first one.
            var flushCommit = preparedFlush ??= BuildFlushCommit();
            session.PrepareQuiescenceFlush(flushCommit.CommitId);

            // Routed through the committer so ownership fencing gates the single durable write. The coalescing
            // commit-store decorator recognises the CoalescedFlush marker, applies the folded state to the durable
            // inner store, advances the durable queue, and deactivates the session.
            await checkpointCommitter.CommitAsync(flushCommit, cancellationToken);
        }

        private RuntimeCheckpointCommit BuildFlushCommit()
        {
            var foldedState = session.FoldBufferedStateChanges();

            // Continuation intents (EnqueueSchedulerWork) were consumed in-segment against the overlay outbox and are
            // already Delivered; only still-pending external intents remain. Re-issue them as intents on the flush
            // commit so the committer folds them into the atomic durable write and they deliver post-flush (condition D).
            // Items an earlier cap flush already persisted durably are excluded: re-issuing them would mint a second
            // outbox item id for the same intent (the id derives from the commit id) and double-deliver it; their
            // durable Pending row already is the post-drain delivery source.
            var remainingIntents = session.RemainingPendingOutboxChanges()
                .Where(change => !session.IsOutboxDurablyPersisted(change.StateId))
                .Select(change => change.State.Intent)
                .ToArray();

            var now = timeProvider.GetUtcNow();
            var checkpoint = new RuntimeCheckpoint(
                CheckpointId: RuntimeExecutionIdGenerator.NewId(now),
                Name: RuntimeCoalescingMetadataKeys.FlushCheckpointName,
                WorkflowExecutionId: session.WorkflowExecutionId,
                OccurredAt: now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true",
                });

            return new RuntimeCheckpointCommit(
                CommitId: RuntimeExecutionIdGenerator.NewId(now),
                Checkpoint: checkpoint,
                StateChanges: foldedState,
                PostCommitIntents: remainingIntents,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        }

        public ValueTask DisposeAsync()
        {
            session.DisableDurableValuePageReuse();
            scopeHandle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
