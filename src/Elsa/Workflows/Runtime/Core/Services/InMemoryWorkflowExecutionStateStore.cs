using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutionStateStore : IWorkflowExecutionStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WorkflowExecutionState> _states = new(StringComparer.Ordinal);

    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states[state.WorkflowExecutionId] = state;
            return new ValueTask<WorkflowExecutionState>(state);
        }
    }

    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(workflowExecutionId, out var state);
            return new ValueTask<WorkflowExecutionState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<WorkflowExecutionState>>(_states.Values.ToArray());
        }
    }
}
