using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

public sealed class InMemoryActivityExecutionStateStore : InMemoryKeyedStateStore<InMemoryActivityExecutionStateStore.ActivityExecutionStateKey, ActivityExecutionState>, IActivityExecutionStateStore
{
    private readonly object _coordinationBinding = new();
    private InMemoryRuntimeCheckpointStoreState? _checkpointState;
    internal bool Coordinates(InMemoryRuntimeCheckpointStoreState state) => ReferenceEquals(_checkpointState, state);
    internal void BindCheckpointState(InMemoryRuntimeCheckpointStoreState state)
    {
        lock (_coordinationBinding)
        {
            if (_checkpointState is not null && !ReferenceEquals(_checkpointState, state))
                throw new NotSupportedException("A native activity store cannot join two checkpoint coordination boundaries.");
            _checkpointState = state;
        }
    }

    internal void DeleteForWorkflowExecution(string workflowExecutionId, InMemoryRuntimeCheckpointStoreState owner)
    {
        if (_checkpointState is not null && !ReferenceEquals(_checkpointState, owner))
            throw new NotSupportedException("Bulk activity cleanup requires the owning checkpoint boundary.");
        var states = SnapshotValues(state =>
            StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId));
        foreach (var state in states)
            Remove(new ActivityExecutionStateKey(workflowExecutionId, state.Execution.ActivityExecutionId));
    }

    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        state.EnsureSupersessionCompatible();

        var key = new ActivityExecutionStateKey(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
        InMemoryRuntimeCheckpointStoreState coordination;
        lock (_coordinationBinding)
        {
            if (_checkpointState is null) return new(Save(key, state));
            coordination = _checkpointState;
        }
        return coordination.ExecuteWriteGateAsync(_ => ValueTask.FromResult(Save(key, state)), cancellationToken);
    }

    public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(new ActivityExecutionStateKey(workflowExecutionId, activityExecutionId)));
    }

    public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new((long)SnapshotValues(state =>
            StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId)).Count);
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Page(
            query,
            $"activity-state:workflow:{query.WorkflowExecutionId}",
            SnapshotValues(state =>
                StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, query.WorkflowExecutionId))));
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Page(
            query,
            $"activity-state:parent:{query.WorkflowExecutionId}:{query.ParentActivityExecutionId}",
            SnapshotValues(state =>
                StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, query.WorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, query.ParentActivityExecutionId))));
    }

    public readonly record struct ActivityExecutionStateKey(string WorkflowExecutionId, string ActivityExecutionId);

    private static RuntimeStorePage<ActivityExecutionState> Page(
        RuntimeStorePageRequest request,
        string queryBinding,
        IEnumerable<ActivityExecutionState> states)
    {
        var continuationId = InMemoryRuntimeStoreContinuation.Decode(
            request.ContinuationToken,
            queryBinding,
            nameof(request));
        var remaining = states
            .OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .Where(state =>
                continuationId is null ||
                StringComparer.Ordinal.Compare(state.Execution.ActivityExecutionId, continuationId) > 0)
            .Take(request.Limit + 1)
            .ToArray();
        var items = remaining.Take(request.Limit).ToArray();
        var nextContinuationToken = remaining.Length > items.Length
            ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].Execution.ActivityExecutionId)
            : null;
        return new RuntimeStorePage<ActivityExecutionState>(request, items, nextContinuationToken);
    }
}
