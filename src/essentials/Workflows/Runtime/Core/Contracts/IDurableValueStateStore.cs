using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for declared durable values.
/// </summary>
public interface IDurableValueStateStore
{
    /// <summary>
    /// Inserts or replaces state for the durable value key.
    /// </summary>
    ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes state for the given workflow execution ID and durable value ID.
    /// </summary>
    ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the durable value state for the given workflow execution ID and durable value ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite, deterministic live-view page of durable value states for the given workflow execution ID.
    /// </summary>
    ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
        DurableValueStatePageQuery query,
        CancellationToken cancellationToken = default);
}
