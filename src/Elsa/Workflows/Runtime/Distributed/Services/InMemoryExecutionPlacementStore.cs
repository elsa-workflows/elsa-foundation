using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// In-memory <see cref="IExecutionPlacementStore"/> for single-process composition and the two-node test harness.
/// Scoped, partition-bound adapters coordinate through singleton shared state; <see cref="TryClaimAsync"/> is serialized
/// under a lock so the compare-and-swap that grants or denies placement is atomic across nodes. The opt-in Groundwork
/// persistence feature replaces this adapter with its durable scoped implementation.
/// </summary>
public sealed class InMemoryExecutionPlacementStore : IExecutionPlacementStore
{
    private readonly InMemoryExecutionPlacementState _state;
    private readonly string _partition;

    public InMemoryExecutionPlacementStore()
        : this(new InMemoryExecutionPlacementState(), new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue))
    {
    }

    internal InMemoryExecutionPlacementStore(
        InMemoryExecutionPlacementState state,
        IPersistenceAccessContextAccessor accessContextAccessor)
        : this(state, InMemoryExecutionPartitionResolver.Resolve(accessContextAccessor))
    {
    }

    private InMemoryExecutionPlacementStore(
        InMemoryExecutionPlacementState state,
        WorkflowExecutionPartition partition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(partition);
        _state = state;
        _partition = partition.Value;
    }

    public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();

        _state.Leases.TryGetValue(Key(workflowExecutionId), out var lease);
        return new ValueTask<ExecutionPlacementLease?>(lease);
    }

    public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var key = Key(claim.WorkflowExecutionId);
            _state.Leases.TryGetValue(key, out var current);

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

            _state.Leases[key] = lease;
            return new ValueTask<ExecutionPlacementClaimResult>(new ExecutionPlacementClaimResult(outcome, lease));
        }
    }

    public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var key = Key(lease.WorkflowExecutionId);
            if (_state.Leases.TryGetValue(key, out var current) &&
                StringComparer.Ordinal.Equals(current.OwnerId, lease.OwnerId) &&
                current.PlacementToken == lease.PlacementToken)
            {
                _state.Leases.TryRemove(KeyValuePair.Create(key, current));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(
        ExecutionPlacementLeaseListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ExecutionPlacementLease> leases = OrderedLeases()
            .Where(lease =>
                StringComparer.Ordinal.Equals(lease.OwnerId, request.OwnerId) &&
                !lease.IsExpired(request.Now))
            .OrderBy(lease => lease.ExpiresAt)
            .ThenBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal)
            .Take(request.Take)
            .ToArray();
        return new ValueTask<IReadOnlyList<ExecutionPlacementLease>>(leases);
    }

    private IEnumerable<ExecutionPlacementLease> OrderedLeases() =>
        _state.Leases
            .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Partition, _partition))
            .Select(pair => pair.Value)
            .OrderBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal);

    private InMemoryExecutionPlacementKey Key(string workflowExecutionId) => new(_partition, workflowExecutionId);
}

internal sealed class InMemoryExecutionPlacementState
{
    public object SyncRoot { get; } = new();
    public ConcurrentDictionary<InMemoryExecutionPlacementKey, ExecutionPlacementLease> Leases { get; } = new();
}

internal readonly record struct InMemoryExecutionPlacementKey(string Partition, string WorkflowExecutionId);

internal static class InMemoryExecutionPartitionResolver
{
    public static WorkflowExecutionPartition Resolve(IPersistenceAccessContextAccessor accessContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        var scope = accessContextAccessor.Current.Scope
            ?? throw new InvalidOperationException("In-memory distributed stores require a scoped persistence access context.");
        return new WorkflowExecutionPartition(scope.Value);
    }
}
