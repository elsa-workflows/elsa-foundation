using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IWorkflowSchedulerWorkQueue"/>. While a coalescing session owns the target
/// workflow execution, enqueue/list/delivery operate on the session's in-memory overlay queue (seeded once from the
/// durable inner queue), so continuation advances the drain without durably dequeuing the segment-entry items. When no
/// session is active it is a byte-for-byte pass-through to the durable inner queue.
/// </summary>
/// <remarks>
/// Claims acquired from the overlay continue to renew, complete, or release against it after a boundary deactivates
/// coalescing. New delivery after deactivation uses the durable inner provider and advertises only capabilities that
/// provider actually implements.
/// </remarks>
public sealed class CoalescingWorkflowSchedulerWorkQueue(
    CoalescingInner<IWorkflowSchedulerWorkQueue> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IWorkflowSchedulerWorkQueue
{
    private readonly IWorkflowSchedulerWorkQueue _inner = inner.Value;

    public bool SupportsClaimTransitions =>
        sessionAccessor.Current is { IsActive: true } || _inner.SupportsClaimTransitions;

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

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(request.WorkflowExecutionId))
            return await session.ClaimOverlayAsync(request, cancellationToken);

        return await _inner.ClaimAsync(request, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.OwnsOverlayClaim(claim))
            return await session.RenewOverlayClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        return await _inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.OwnsOverlayClaim(claim))
            return await session.CompleteOverlayClaimAsync(claim, cancellationToken);

        return await _inner.CompleteClaimAsync(claim, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.OwnsOverlayClaim(claim))
            return await session.ReleaseOverlayClaimAsync(claim, visibleAt, cancellationToken);

        return await _inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
    }
}
