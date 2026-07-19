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
    /// Conditionally creates or replaces operational state. Revision <c>0</c> means create-only; a positive revision
    /// means compare-and-swap against the current provider revision.
    /// </summary>
    ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns operational state for the given workflow execution ID and operational state ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default);

    /// <summary>Returns operational state together with its provider-neutral optimistic-concurrency revision.</summary>
    ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operational states visible to this store.
    /// </summary>
    ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default);
}
