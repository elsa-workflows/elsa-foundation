using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Default <see cref="IRuntimeCoalescingDrainScopeFactory"/>. Creates a <see cref="RuntimeCoalescingSession"/> per drain,
/// pushes it onto the ambient <see cref="IRuntimeCoalescingSessionAccessor"/>, and fails closed at quiescence when the
/// session requires the separately reviewed terminal Prepared-fold work.
/// </summary>
public sealed class RuntimeCoalescingDrainScopeFactory(
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
    CoalescingInner<IRuntimePostCommitOutboxStore> innerOutboxStore,
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
        return new Scope(session, handle);
    }

    private sealed class Scope(
        RuntimeCoalescingSession session,
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

            throw new NotSupportedException(
                "Prepared checkpoint terminal folding is not enabled until the reviewed adoption/fold work unit is approved.");
        }

        public ValueTask DisposeAsync()
        {
            scopeHandle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
