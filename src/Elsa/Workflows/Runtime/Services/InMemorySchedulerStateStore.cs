using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemorySchedulerStateStore : ISchedulerStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, SchedulerState> _states = new(StringComparer.Ordinal);

    public ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states[state.WorkflowExecutionId] = state;
            return new ValueTask<SchedulerState>(state);
        }
    }

    public ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(workflowExecutionId, out var state);
            return new ValueTask<SchedulerState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<SchedulerState>>(_states.Values.ToArray());
        }
    }
}
