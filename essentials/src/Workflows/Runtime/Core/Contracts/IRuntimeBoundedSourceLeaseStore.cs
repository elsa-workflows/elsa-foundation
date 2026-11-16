using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Selected provider capability for source leases whose own reads are scope-qualified and bounded.</summary>
/// <remarks>A cooperating decorator must explicitly preserve all three operations. Ordinary lease methods are not a
/// fallback: their provider reads may hydrate an unbounded replacement row before a later exact-pin check.</remarks>
public interface IRuntimeBoundedSourceLeaseStore
{
    ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireAsync(string artifactId, string leaseId, DateTimeOffset expiresAt,
        DateTimeOffset now, PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default);
    ValueTask<bool> RenewAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now,
        PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default);
    ValueTask ReleaseAsync(WorkflowExecutableRootWriteLease lease, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default);
}
