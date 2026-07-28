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
    /// Returns the number of activity execution states owned by one workflow execution without materializing
    /// their payloads. Durable providers should implement this as a provider-side count over the workflow index.
    /// </summary>
    async ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        long count = 0;
        string? continuationToken = null;
        do
        {
            var page = await ListPageAsync(
                new ActivityExecutionStatePageQuery(workflowExecutionId, continuationToken: continuationToken),
                cancellationToken);
            count += page.Items.Count;
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return count;
    }

    /// <summary>
    /// Returns one finite page of activity execution states for the given workflow execution ID.
    /// </summary>
    ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite page of activity execution states directly parented within the
    /// given workflow execution. This is the parent-scoped read the whole-workflow <see cref="ListPageAsync"/>
    /// would otherwise be filtered down to; it lets callers that only need a composite's direct children (e.g. the Parallel
    /// fork/join counting the completed branches) avoid loading and filtering every activity-execution state in the workflow.
    /// The result is exactly the subset of <see cref="ListPageAsync"/> whose
    /// <see cref="ActivityExecutionState.ParentActivityExecutionId"/> equals
    /// <see cref="ActivityExecutionStateParentPageQuery.ParentActivityExecutionId"/>.
    /// </summary>
    ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default);
}
