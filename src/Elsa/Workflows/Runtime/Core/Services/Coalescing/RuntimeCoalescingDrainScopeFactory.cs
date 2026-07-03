using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Default <see cref="IRuntimeCoalescingDrainScopeFactory"/>. Creates a <see cref="RuntimeCoalescingSession"/> per drain,
/// pushes it onto the ambient <see cref="IRuntimeCoalescingSessionAccessor"/>, and flushes the folded segment through
/// the <see cref="RuntimeCheckpointCommitter"/> at quiescence so W5 ownership fencing gates the single durable write.
/// </summary>
public sealed class RuntimeCoalescingDrainScopeFactory(
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    RuntimeCheckpointCommitter checkpointCommitter,
    CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
    CoalescingRuntimeCheckpointPersistenceOptions options,
    TimeProvider timeProvider) : IRuntimeCoalescingDrainScopeFactory
{
    public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var session = new RuntimeCoalescingSession(workflowExecutionId, innerQueue.Value, options);
        var handle = sessionAccessor.Push(session);
        return new Scope(session, handle, checkpointCommitter, timeProvider);
    }

    private sealed class Scope(
        RuntimeCoalescingSession session,
        IDisposable scopeHandle,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider) : IRuntimeCoalescingDrainScope
    {
        public RuntimeCoalescingSession Session => session;

        public async ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default)
        {
            // Already flushed at a boundary (or cap) during the drain: nothing more to coalesce.
            if (!session.IsActive)
                return;

            // Active but empty: the drain consumed queue items without buffering any deferred checkpoint. Reconcile the
            // durable queue with the overlay consumption and end the segment; no folded commit is required.
            if (!session.HasBufferedChanges)
            {
                await session.AdvanceInnerQueueAsync(cancellationToken);
                session.ClearBuffer();
                session.Deactivate();
                return;
            }

            var flushCommit = BuildFlushCommit();

            // Routed through the committer so ownership fencing (W5) gates the single durable write. The coalescing
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
            var remainingIntents = session.RemainingPendingOutboxChanges()
                .Select(change => change.State.Intent)
                .ToArray();

            var now = timeProvider.GetUtcNow();
            var checkpoint = new RuntimeCheckpoint(
                CheckpointId: Guid.NewGuid().ToString("N"),
                Name: RuntimeCoalescingMetadataKeys.FlushCheckpointName,
                WorkflowExecutionId: session.WorkflowExecutionId,
                OccurredAt: now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true",
                });

            return new RuntimeCheckpointCommit(
                CommitId: Guid.NewGuid().ToString("N"),
                Checkpoint: checkpoint,
                StateChanges: foldedState,
                PostCommitIntents: remainingIntents,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        }

        public ValueTask DisposeAsync()
        {
            scopeHandle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
