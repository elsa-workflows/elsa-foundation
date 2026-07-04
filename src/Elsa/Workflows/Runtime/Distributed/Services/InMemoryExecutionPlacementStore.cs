using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// In-memory <see cref="IExecutionPlacementStore"/> for single-process composition and the two-node test harness. A
/// single instance shared by every node container is the cluster's placement authority; <see cref="TryClaimAsync"/> is
/// serialized under a lock so the compare-and-swap that grants/denies placement is atomic across nodes. A durable
/// (e.g. Groundwork) implementation is a named follow-up.
/// </summary>
public sealed class InMemoryExecutionPlacementStore : IExecutionPlacementStore
{
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, ExecutionPlacementLease> _leasesByExecutionId = new(StringComparer.Ordinal);

    public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        _leasesByExecutionId.TryGetValue(workflowExecutionId, out var lease);
        return new ValueTask<ExecutionPlacementLease?>(lease);
    }

    public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _leasesByExecutionId.TryGetValue(claim.WorkflowExecutionId, out var current);

            // A live lease held by another node blocks the claim; placement stays with the current owner.
            if (current is not null && !current.IsExpired(now) && !StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId))
                return new ValueTask<ExecutionPlacementClaimResult>(new ExecutionPlacementClaimResult(ExecutionPlacementClaimOutcome.Denied, current));

            var outcome = current is not null && StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId) && !current.IsExpired(now)
                ? ExecutionPlacementClaimOutcome.Renewed
                : ExecutionPlacementClaimOutcome.Granted;

            var nextToken = (current?.PlacementToken ?? 0) + 1;
            var lease = new ExecutionPlacementLease(
                workflowExecutionId: claim.WorkflowExecutionId,
                ownerId: claim.OwnerId,
                placementToken: nextToken,
                acquiredAt: claim.RequestedAt,
                expiresAt: claim.ExpiresAt);

            _leasesByExecutionId[claim.WorkflowExecutionId] = lease;
            return new ValueTask<ExecutionPlacementClaimResult>(new ExecutionPlacementClaimResult(outcome, lease));
        }
    }

    public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_leasesByExecutionId.TryGetValue(lease.WorkflowExecutionId, out var current) &&
                StringComparer.Ordinal.Equals(current.OwnerId, lease.OwnerId) &&
                current.PlacementToken == lease.PlacementToken)
            {
                _leasesByExecutionId.TryRemove(KeyValuePair.Create(lease.WorkflowExecutionId, current));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<ExecutionPlacementLease>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<ExecutionPlacementLease> leases = _leasesByExecutionId.Values
            .OrderBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyCollection<ExecutionPlacementLease>>(leases);
    }
}
