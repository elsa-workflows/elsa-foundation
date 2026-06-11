using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for concrete activity executions.
/// </summary>
public interface IActivityExecutionStateStore
{
    /// <summary>
    /// Inserts or replaces state for the concrete activity execution key.
    /// </summary>
    ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the state for the given workflow execution ID and activity execution ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all activity execution states for the given workflow execution ID.
    /// </summary>
    ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
