using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for runtime-owned operational coordination.
/// </summary>
public interface IOperationalStateStore
{
    /// <summary>
    /// Inserts or replaces operational state for the operational state key.
    /// </summary>
    ValueTask<OperationalState> SaveAsync(OperationalState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns operational state for the given workflow execution ID and operational state ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<OperationalState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<OperationalState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states visible to this store.
    /// </summary>
    ValueTask<IReadOnlyCollection<OperationalState>> ListAllAsync(CancellationToken cancellationToken = default);
}
