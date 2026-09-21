using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Options;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// Default <see cref="IExecutionPlacementService"/>. Stamps placement claims with this node's <see cref="NodeId"/> and
/// an expiry computed from <see cref="ExecutionPlacementOptions.LeaseDuration"/> against an injected
/// <see cref="TimeProvider"/>, then delegates the atomic compare-and-swap to the shared
/// <see cref="IExecutionPlacementStore"/>.
/// </summary>
public sealed class ExecutionPlacementService : IExecutionPlacementService
{
    private readonly IExecutionPlacementStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ExecutionPlacementOptions _options;

    public ExecutionPlacementService(IExecutionPlacementStore store, TimeProvider timeProvider, ExecutionPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        DistributedRuntimeIdentityConstraints.Validate(options.NodeId, nameof(options.NodeId));

        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Placement lease duration must be greater than zero.");

        _store = store;
        _timeProvider = timeProvider;
        _options = options;
    }

    public string NodeId => _options.NodeId;

    public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));

        var now = _timeProvider.GetUtcNow();
        var claim = new ExecutionPlacementClaim(
            workflowExecutionId: workflowExecutionId,
            ownerId: _options.NodeId,
            requestedAt: now,
            expiresAt: now + _options.LeaseDuration);

        return _store.TryClaimAsync(claim, now, cancellationToken);
    }

    public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return _store.ReleaseAsync(lease, cancellationToken);
    }

    public ValueTask<ExecutionPlacementLease?> FindOwnerAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        return _store.FindAsync(workflowExecutionId, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ExecutionPlacementLease>> ListOwnedAsync(
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        return await _store.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest(_options.NodeId, now, maxItems),
            cancellationToken);
    }
}
