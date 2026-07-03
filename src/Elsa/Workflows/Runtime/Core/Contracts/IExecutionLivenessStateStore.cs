using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for runtime-owned operational coordination.
/// </summary>
public interface IExecutionLivenessStateStore
{
    /// <summary>
    /// Inserts or replaces operational state for the operational state key.
    /// </summary>
    ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns operational state for the given workflow execution ID and operational state ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states visible to this store.
    /// </summary>
    ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default);
}
