using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Durable, cluster-shared placement authority for execution leases. This is the atomicity boundary for placement:
/// implementations coordinate through shared backing state or storage and apply <see cref="TryClaimAsync"/> as a
/// compare-and-swap so two nodes cannot both hold a live placement lease for the same execution. The per-node
/// <see cref="IExecutionPlacementService"/> supplies policy (node id, lease duration) on top of this contract.
/// </summary>
/// <remarks>
/// Placement is routing, not correctness — see <see cref="ExecutionPlacementLease"/>. A default in-memory
/// implementation ships for single-process composition and the two-node test harness. The opt-in Groundwork persistence
/// feature supplies a durable implementation behind this contract without touching Runtime.Core.
/// </remarks>
public interface IExecutionPlacementStore
{
    /// <summary>Returns the current placement lease for the execution, or <see langword="null"/> when unplaced.</summary>
    ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically grants, renews, or denies a placement claim against the current lease, evaluated at <paramref name="now"/>:
    /// granted when unplaced or the current lease has expired; renewed when the current live lease belongs to the claimant;
    /// denied when a live lease belongs to another node. A granted/renewed lease carries a strictly greater placement token.
    /// </summary>
    ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the placement lease only when the stored lease still matches the caller's owner id and placement token,
    /// so a superseded holder cannot clear a newer owner's placement. A no-op otherwise.
    /// </summary>
    ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default);

    /// <summary>Lists all currently stored placement leases (used by a node's pump to discover the executions it owns).</summary>
    ValueTask<IReadOnlyCollection<ExecutionPlacementLease>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IPagedExecutionPlacementStore
{
    /// <summary>
    /// Lists stored placement leases through a provider-bounded deterministic page. The page is scoped by the current
    /// persistence operation scope and ordered by workflow execution id.
    /// </summary>
    ValueTask<ExecutionPlacementLeasePage> ListPageAsync(ExecutionPlacementLeasePageRequest request, CancellationToken cancellationToken = default);
}

public sealed record ExecutionPlacementLeasePageRequest(int Skip = 0, int Take = 100)
{
    public int NormalizedSkip => Math.Max(Skip, 0);
    public int NormalizedTake => Math.Clamp(Take, 1, 500);
}

public sealed record ExecutionPlacementLeasePage(IReadOnlyCollection<ExecutionPlacementLease> Items, long TotalCount);
