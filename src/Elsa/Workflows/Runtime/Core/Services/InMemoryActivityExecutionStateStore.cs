using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryActivityExecutionStateStore : IActivityExecutionStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ActivityExecutionStateKey, ActivityExecutionState> _states = new();

    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new ActivityExecutionStateKey(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
            if (_states.TryGetValue(key, out var existing))
                return new ValueTask<ActivityExecutionState>(existing);

            _states.Add(key, state);
            return new ValueTask<ActivityExecutionState>(state);
        }
    }

    public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(new ActivityExecutionStateKey(workflowExecutionId, activityExecutionId), out var state);
            return new ValueTask<ActivityExecutionState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<ActivityExecutionState>>(states);
        }
    }

    private readonly record struct ActivityExecutionStateKey(string WorkflowExecutionId, string ActivityExecutionId);
}
