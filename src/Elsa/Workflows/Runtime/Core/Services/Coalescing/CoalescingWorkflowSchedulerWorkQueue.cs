using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IWorkflowSchedulerWorkQueue"/>. While a coalescing session owns the target
/// workflow execution, enqueue/list/dequeue operate on the session's in-memory overlay queue (seeded once from the
/// durable inner queue), so continuation advances the drain without durably dequeuing the segment-entry items. When no
/// session is active it is a byte-for-byte pass-through to the durable inner queue.
/// </summary>
/// <remarks>
/// The drainer's peek/pause/dequeue/TOCTOU sequence is unchanged: it peeks and dequeues against this same decorated
/// queue, which resolves both operations against the same instance (overlay or inner), so the single-writer tripwire
/// remains active and consistent against whichever queue is resolved.
/// </remarks>
public sealed class CoalescingWorkflowSchedulerWorkQueue(
    CoalescingInner<IWorkflowSchedulerWorkQueue> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IWorkflowSchedulerWorkQueue
{
    private readonly IWorkflowSchedulerWorkQueue _inner = inner.Value;

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (sessionAccessor.Current is { } session && session.AppliesTo(workItem.WorkflowExecutionId))
            return await session.EnqueueOverlayAsync(workItem, cancellationToken);

        return await _inner.EnqueueAsync(workItem, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (sessionAccessor.Current is { } session && session.AppliesTo(query.WorkflowExecutionId))
            return await session.ListOverlayAsync(query, cancellationToken);

        return await _inner.ListAsync(query, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return await session.DequeueOverlayAsync(cancellationToken);

        return await _inner.DequeueAsync(workflowExecutionId, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
        _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
}
