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
}
