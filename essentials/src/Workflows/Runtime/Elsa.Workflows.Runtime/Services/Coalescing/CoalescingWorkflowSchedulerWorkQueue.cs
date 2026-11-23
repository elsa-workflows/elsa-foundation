using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

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
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IWorkflowSchedulerWorkQueue, IRuntimeSchedulerWorkDeferralCapability
{
    private readonly IWorkflowSchedulerWorkQueue _inner = inner.Value;

    public bool SupportsClaimTransitions =>
        sessionAccessor.Current is { IsActive: true } || _inner.SupportsClaimTransitions;

    // A nested execution can bypass the current execution's overlay. Never credit an unknown inner provider
    // merely because some other execution has an active in-memory segment in this async context.
    public bool SupportsClaimValidation => _inner.SupportsClaimValidation;

    bool IRuntimeSchedulerWorkDeferralCapability.CanDefer(RuntimeSchedulerWorkClaim claim) =>
        sessionAccessor.Current is not { } session || !session.OwnsOverlayClaim(claim) || session.SupportsDeferredOverlayRelease;

    public bool SupportsTargetedDeletion => _inner.SupportsTargetedDeletion;

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (sessionAccessor.Current is { } session && session.AppliesTo(workItem.WorkflowExecutionId))
            return await session.EnqueueOverlayAsync(workItem, cancellationToken);

        return await _inner.EnqueueAsync(workItem, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
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

    // Backlog discovery runs in the resumption sweep, outside any coalescing session, so like the listing above it
    // has no overlay to consult. Forwarding the capability keeps the sweep on the provider's claimable discovery.
    public bool SupportsClaimableBacklogDiscovery => _inner.SupportsClaimableBacklogDiscovery;

    public ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
        RuntimeSchedulerClaimableBacklogQuery query,
        CancellationToken cancellationToken = default) =>
        _inner.ListClaimableWorkflowExecutionIdsAsync(query, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
        IReadOnlyCollection<string> workflowExecutionIds,
        CancellationToken cancellationToken = default) =>
        _inner.ListNextWorkItemsAsync(workflowExecutionIds, cancellationToken);

    // Targeted deletion addresses durable residue, including the out-of-drain terminal purge.
    // The session's own reconciliation uses its captured inner provider after the folded checkpoint commits.
    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(request.WorkflowExecutionId))
        {
            var head = await session.ListOverlayAsync(
                new RuntimeSchedulerWorkQuery(request.WorkflowExecutionId, limit: 1),
                cancellationToken);
            if (head.Items.FirstOrDefault() is { } item && request.ValidateBeforeClaim is { } validate)
                await validate(item, cancellationToken);
            if (!session.SupportsDeferredOverlayRelease &&
                head.Items.FirstOrDefault()?.CommandKind == WorkflowExecutionCommandKind.ResumeBookmark)
            {
                // Legacy queues keep the established bookmark-resume durable boundary. Other work remains
                // coalescible and propagates unavailable when its overlay cannot persist a delay. Flush the current
                // coalesced segment before claiming this item so the drainer's later release updates the real durable
                // queue row rather than only an overlay that disappears at quiescence.
                session.RequestDurableSchedulerClaimBoundary();
                return null;
            }

            return await session.ClaimOverlayAsync(request, cancellationToken);
        }

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

    // Atomic checkpoint consumption only reaches the durable queue when no session folds the execution's checkpoints:
    // the committer suppresses the consume-fold while a coalescing session owns the execution, so this is always a
    // pass-through to the durable inner queue.
    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default) =>
        _inner.ConsumeClaimedAsync(consumed, cancellationToken);
}
