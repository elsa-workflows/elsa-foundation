using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>Marks the command transport as a single-implementation replacement contract.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ExecutionCommandTransportReplacementContractAttribute : Attribute
{
}

/// <summary>
/// Durable cross-node command inbox for workflow executions. When a command arrives on a node that does not own an
/// execution's placement, it is sent here; the owning node's placement pump leases pending items and dispatches them to
/// its local in-process actor. This is the routing substrate — it decides <em>where</em> a command runs, not whether a
/// commit is safe (that is the single-writer fencing lease, enforced at checkpoint commit).
/// </summary>
/// <remarks>
/// Delivery is <b>at-least-once</b>. Dequeue is ack-based, not destructive-before-dispatch: <see cref="LeaseAsync"/>
/// hides an item behind a visibility lease instead of removing it, and only <see cref="AckAsync"/> removes it after the
/// owning node has dispatched and durably committed. If a node dies after leasing but before ack, the lease expires and
/// the item becomes visible again, so the survivor that claims placement re-leases and re-drives it on failover. This
/// mirrors the runtime's queue semantics and the ack-based hold-until-commit dequeue recorded in
/// <c>docs/runtime-durable-resumption.md</c>; the fencing token checked at checkpoint commit is what prevents a
/// re-driven command from producing a second durable execution.
/// </remarks>
/// <remarks>
/// Persistence leaves replace exactly this contract. They do not replace execution placement, checkpoint, outbox, or
/// lease-fencing services; those remain independently owned composition units during the opt-in migration.
/// </remarks>
[ExecutionCommandTransportReplacementContract]
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
    /// Removes an item after successful dispatch + durable commit. The acknowledgement must carry the exact
    /// <paramref name="leaseToken"/> returned by <see cref="LeaseAsync"/>. Returns <c>false</c> when the item is no
    /// longer held by <paramref name="ownerId"/> with that token (e.g. its lease expired and another node re-leased
    /// it), so a superseded node cannot ack away a command the survivor is now responsible for.
    /// </summary>
    ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes an item visible again before its lease expires, when <paramref name="ownerId"/> still holds it under
    /// <paramref name="leaseToken"/> and the lease is live, as a compare-and-set (spec 184, FR-019 and FR-024). Returns
    /// whether this call released it. The released holder's acknowledgement is refused afterwards, and the next lease
    /// issues a greater token.
    /// </summary>
    ValueTask<bool> ReleaseLeaseAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="maxItems"/> items for <paramref name="workflowExecutionId"/> that are visible at
    /// <paramref name="now"/>, in enqueue order, without leasing them. Placement reads the pinned executable of pending
    /// work through it before it decides whether to claim (spec 184, FR-016), so a refused claim leases nothing.
    /// </summary>
    ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> PeekAsync(string workflowExecutionId, DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="maxItems"/> items whose live lease <paramref name="ownerId"/> holds at
    /// <paramref name="now"/>, optionally for one execution, so a hand-off or a reclaim can release them (spec 184,
    /// FR-019 and FR-024). A store may answer it without an owner index: it runs only when a member hands work off or a
    /// host id departs.
    /// </summary>
    ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> ListLeasedAsync(string ownerId, DateTimeOffset now, int maxItems, string? workflowExecutionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists at most <paramref name="maxItems"/> execution IDs that currently have at least one visible item, in
    /// deterministic ordinal order, for the placement pump's bounded backlog sweep.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists, as <see cref="ListPendingExecutionIdsAsync(DateTimeOffset, int, CancellationToken)"/> does, the execution
    /// IDs that follow the first <paramref name="skip"/> in the same order. The placement pump rotates through the
    /// backlog with it, because work no active member can run stays pending (spec 184, FR-018) and must not starve the
    /// work behind it. The backlog moves between calls, so a rotation may miss or repeat an id once; the next reaches it.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        int skip,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the number of undelivered (not-yet-acked) items for an execution, regardless of lease state. Primarily for diagnostics and tests.</summary>
    ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
