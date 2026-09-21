using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for execution-affecting incidents.
/// </summary>
public interface IIncidentStateStore
{
    /// <summary>
    /// Inserts state for a newly observed incident. Returns <see langword="false"/> when the incident key already exists.
    /// </summary>
    ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or replaces state for the incident key.
    /// </summary>
    ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the incident state for the given workflow execution ID and incident ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of incidents for one workflow execution. Durable providers should implement this as
    /// a provider-side count over the workflow index.
    /// </summary>
    async ValueTask<int> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return (await ListAsync(workflowExecutionId, cancellationToken)).Count;
    }

    /// <summary>
    /// Returns all incident states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all blocking incident states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
