using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores runtime-owned administrative control-plane state outside workflow continuation state.
/// </summary>
public interface IWorkflowHoldStateStore
{
    /// <summary>
    /// Inserts or replaces control-plane state for the control-plane state key.
    /// </summary>
    ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns control-plane state for the given control-plane state ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns workflow-scoped control-plane states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all control-plane states visible to this store.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default);
}
