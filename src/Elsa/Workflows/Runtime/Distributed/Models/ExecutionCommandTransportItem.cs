using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Models;

/// <summary>
/// One durable command queued for a workflow execution in the cross-node transport inbox. The item carries the existing
/// <see cref="WorkflowExecutionCommandEnvelope"/> losslessly (any <see cref="WorkflowExecutionCommandKind"/>) plus the
/// queue-management state that makes delivery ack-based rather than destructive-on-dequeue.
/// </summary>
/// <remarks>
/// Delivery is <b>at-least-once</b> with lease/visibility semantics. <see cref="LeaseAsync"/>-style dequeue does not
/// remove the item; it stamps <see cref="LeasedByOwnerId"/> and <see cref="LeaseExpiresAt"/> so the item is invisible
/// to other nodes until the lease is acked (removed) or expires. If the owning node dies mid-dequeue — after leasing
/// but before ack — the lease simply expires and the item becomes visible again, so the survivor that claims placement
/// re-leases and re-drives it on failover. This mirrors the ack-based hold-until-commit dequeue that
/// <c>docs/runtime-durable-resumption.md</c> records as the remaining increment on W2's durable queue, and matches the
/// same at-least-once concern W5 recorded: destructive-before-dispatch would drop a command when a node dies mid-flight,
/// so this transport deliberately does not remove before ack.
/// </remarks>
public sealed class ExecutionCommandTransportItem
{
    public ExecutionCommandTransportItem(
        string transportItemId,
        string workflowExecutionId,
        WorkflowExecutionCommandEnvelope envelope,
        long sequence,
        DateTimeOffset enqueuedAt,
        int deliveryAttemptCount = 0,
        string? leasedByOwnerId = null,
        DateTimeOffset? leaseExpiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentNullException.ThrowIfNull(envelope);

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Transport sequence cannot be negative.");

        if (deliveryAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryAttemptCount), "Delivery attempt count cannot be negative.");

        if (!string.Equals(workflowExecutionId, envelope.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Transport item workflow execution ID must match the envelope workflow execution ID.", nameof(workflowExecutionId));

        if (leasedByOwnerId is not null)
            DistributedRuntimeIdentityConstraints.Validate(leasedByOwnerId, nameof(leasedByOwnerId));

        if (leasedByOwnerId is null ^ leaseExpiresAt is null)
            throw new ArgumentException("A leased transport item must carry both an owner ID and a lease expiry, or neither.", nameof(leasedByOwnerId));

        TransportItemId = transportItemId;
        WorkflowExecutionId = workflowExecutionId;
        Envelope = envelope;
        Sequence = sequence;
        EnqueuedAt = enqueuedAt;
        DeliveryAttemptCount = deliveryAttemptCount;
        LeasedByOwnerId = leasedByOwnerId;
        LeaseExpiresAt = leaseExpiresAt;
    }

    public string TransportItemId { get; }
    public string WorkflowExecutionId { get; }
    public WorkflowExecutionCommandEnvelope Envelope { get; }
    public long Sequence { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public int DeliveryAttemptCount { get; }
    public string? LeasedByOwnerId { get; }
    public DateTimeOffset? LeaseExpiresAt { get; }

    /// <summary>An item is visible (available to lease) when it is unleased or its lease has expired.</summary>
    public bool IsVisible(DateTimeOffset now) => LeasedByOwnerId is null || LeaseExpiresAt is null || LeaseExpiresAt.Value <= now;

    /// <summary>Returns a copy leased by <paramref name="ownerId"/> until <paramref name="leaseExpiresAt"/>, bumping the attempt count.</summary>
    public ExecutionCommandTransportItem Lease(string ownerId, DateTimeOffset leaseExpiresAt) => new(
        TransportItemId,
        WorkflowExecutionId,
        Envelope,
        Sequence,
        EnqueuedAt,
        DeliveryAttemptCount + 1,
        ownerId,
        leaseExpiresAt);
}
