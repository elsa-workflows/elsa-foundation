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

    /// <summary>
    /// Returns the activity execution states directly parented by <paramref name="parentActivityExecutionId"/> within the
    /// given workflow execution. This is the parent-scoped read the whole-workflow <see cref="ListAsync(string, CancellationToken)"/>
    /// would otherwise be filtered down to; it lets callers that only need a composite's direct children (e.g. the Parallel
    /// fork/join counting the completed branches) avoid loading and filtering every activity-execution state in the workflow.
    /// The result is exactly the subset of <see cref="ListAsync(string, CancellationToken)"/> whose
    /// <see cref="ActivityExecutionState.ParentActivityExecutionId"/> equals <paramref name="parentActivityExecutionId"/>.
    /// </summary>
    ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListByParentAsync(string workflowExecutionId, string parentActivityExecutionId, CancellationToken cancellationToken = default);
}
