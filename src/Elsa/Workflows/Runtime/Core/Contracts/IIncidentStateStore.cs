using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for execution-affecting incidents.
/// </summary>
public interface IIncidentStateStore
{
    /// <summary>
    /// Inserts or replaces state for the incident key.
    /// </summary>
    ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the incident state for the given workflow execution ID and incident ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all incident states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all blocking incident states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
