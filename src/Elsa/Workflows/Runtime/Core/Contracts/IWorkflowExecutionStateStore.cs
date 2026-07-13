using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for workflow executions.
/// </summary>
public interface IWorkflowExecutionStateStore
{
    /// <summary>
    /// Inserts or replaces state for the concrete workflow execution key.
    /// </summary>
    ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the state for the given workflow execution ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all workflow execution states currently held by the store.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries a bounded, stably ordered page of retained workflow execution state without requiring callers to
    /// materialize the complete history.
    /// </summary>
    ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct executable artifact IDs pinned by retained workflow executions.
    /// </summary>
    /// <remarks>
    /// Every retained execution is a retention root, including executions in a terminal status. The result is a
    /// projection of those roots rather than a request to materialize complete workflow execution states.
    /// </remarks>
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the state for the given workflow execution ID.
    /// </summary>
    /// <returns><see langword="true"/> when a retained execution was deleted; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
