using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryOperationalStateStore : IOperationalStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<OperationalStateKey, OperationalState> _states = new();

    public ValueTask<OperationalState> SaveAsync(OperationalState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new OperationalStateKey(state.WorkflowExecutionId, state.OperationalStateId);
            _states[key] = state;
            return new ValueTask<OperationalState>(state);
        }
    }

    public ValueTask<OperationalState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(new OperationalStateKey(workflowExecutionId, operationalStateId), out var state);
            return new ValueTask<OperationalState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<OperationalState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<OperationalState>>(states);
        }
    }

    private readonly record struct OperationalStateKey(string WorkflowExecutionId, string OperationalStateId);
}
