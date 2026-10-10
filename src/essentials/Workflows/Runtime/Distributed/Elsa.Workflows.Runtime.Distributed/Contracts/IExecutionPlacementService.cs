using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Per-node policy over <see cref="IExecutionPlacementStore"/>: builds placement claims from this node's identity and
/// lease duration and interprets the store's compare-and-swap result. Routing decisions in
/// <c>DistributedWorkflowExecutionActorProvider</c> and the renewal/claim loop in the placement pump go through this
/// service; the shared store owns cross-node atomicity.
/// </summary>
public interface IExecutionPlacementService
{
    /// <summary>
    /// Stable identity of this node as a placement owner, within
    /// <see cref="DistributedRuntimeIdentityConstraints"/>.
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Attempts to claim or renew placement of the execution for this node. Granted/renewed when the execution is
    /// unplaced, held by an expired lease, or already held by this node; denied when another node holds a live lease.
    /// </summary>
    ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a placement lease this node still holds under exactly <paramref name="lease"/>'s placement token, as a
    /// compare-and-set that never grants (spec 184, FR-014). Returns the renewed lease, or <see langword="null"/> when
    /// the lease expired, was released or was taken by another member since.
    /// </summary>
    ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default);

    /// <summary>Releases a placement lease, matched on owner id + placement token. Returns whether this call released it.</summary>
    ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default);

    /// <summary>Returns the current placement lease for the execution, or <see langword="null"/> when unplaced.</summary>
    ValueTask<ExecutionPlacementLease?> FindOwnerAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists at most <paramref name="maxItems"/> executions this node currently holds a non-expired placement lease
    /// for, evaluated now in earliest-expiry order.
    /// </summary>
    ValueTask<IReadOnlyCollection<ExecutionPlacementLease>> ListOwnedAsync(
        int maxItems,
        CancellationToken cancellationToken = default);
}
