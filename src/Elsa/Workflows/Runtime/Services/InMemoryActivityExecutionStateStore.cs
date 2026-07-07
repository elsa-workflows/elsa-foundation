using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryActivityExecutionStateStore : InMemoryKeyedStateStore<InMemoryActivityExecutionStateStore.ActivityExecutionStateKey, ActivityExecutionState>, IActivityExecutionStateStore
{
    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new ActivityExecutionStateKey(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
        return new(Save(key, state));
    }

    public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(new ActivityExecutionStateKey(workflowExecutionId, activityExecutionId)));
    }

    public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Snapshot(key => key.WorkflowExecutionId == workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListByParentAsync(string workflowExecutionId, string parentActivityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotValues(state =>
            StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId) &&
            StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, parentActivityExecutionId)));
    }

    public readonly record struct ActivityExecutionStateKey(string WorkflowExecutionId, string ActivityExecutionId);
}
