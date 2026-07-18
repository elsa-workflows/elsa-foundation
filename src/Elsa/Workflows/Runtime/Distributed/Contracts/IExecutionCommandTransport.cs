using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Durable cross-node command inbox for workflow executions. When a command arrives on a node that does not own an
/// execution's placement, it is sent here; the owning node's placement pump leases pending items and dispatches them to
/// its local in-process actor. This is the routing substrate — it decides <em>where</em> a command runs, not whether a
/// commit is safe (that is W5's fencing lease, enforced at checkpoint commit).
/// </summary>
/// <remarks>
/// Delivery is <b>at-least-once</b>. Dequeue is ack-based, not destructive-before-dispatch: <see cref="LeaseAsync"/>
/// hides an item behind a visibility lease instead of removing it, and only <see cref="AckAsync"/> removes it after the
/// owning node has dispatched and durably committed. If a node dies after leasing but before ack, the lease expires and
/// the item becomes visible again, so the survivor that claims placement re-leases and re-drives it on failover. This
/// mirrors W2's queue semantics and the ack-based hold-until-commit dequeue recorded in
/// <c>docs/runtime-durable-resumption.md</c>; the fencing token checked at checkpoint commit is what prevents a
/// re-driven command from producing a second durable execution.
/// </remarks>
public interface IExecutionCommandTransport
{
    /// <summary>Appends a command for <paramref name="workflowExecutionId"/> to its durable inbox.</summary>
    ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases up to <paramref name="maxItems"/> currently-visible items for <paramref name="workflowExecutionId"/> to
    /// <paramref name="ownerId"/> until <paramref name="now"/> + <paramref name="leaseDuration"/>, in enqueue order.
    /// Leased items are hidden from other nodes until acked or the lease expires.
    /// </summary>
    ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item after successful dispatch + durable commit. Returns <c>false</c> when the item is no longer held
    /// by <paramref name="ownerId"/> (e.g. its lease expired and another node re-leased it), so a superseded node
    /// cannot ack away a command the survivor is now responsible for.
    /// </summary>
    ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists at most <paramref name="maxItems"/> execution IDs that currently have at least one visible item, in
    /// deterministic ordinal order, for the placement pump's bounded backlog sweep.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the number of undelivered (not-yet-acked) items for an execution, regardless of lease state. Primarily for diagnostics and tests.</summary>
    ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
