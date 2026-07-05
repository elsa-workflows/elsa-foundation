using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryDurableValueStateStore : IDurableValueStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<DurableValueStateKey, DurableValueState> _states = new();

    public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DurableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new DurableValueStateKey(state.WorkflowExecutionId, state.DurableValueId);
            _states[key] = state;
            return new ValueTask<DurableValueState>(state);
        }
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<bool>(_states.Remove(new DurableValueStateKey(workflowExecutionId, durableValueId)));
        }
    }

    public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(new DurableValueStateKey(workflowExecutionId, durableValueId), out var state);
            return new ValueTask<DurableValueState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<DurableValueState>>(states);
        }
    }

    private readonly record struct DurableValueStateKey(string WorkflowExecutionId, string DurableValueId);
}
