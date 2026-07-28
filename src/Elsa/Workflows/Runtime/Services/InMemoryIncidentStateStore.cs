using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryIncidentStateStore : IIncidentStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<IncidentStateKey, IncidentState> _states = new();

    public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new IncidentStateKey(state.WorkflowExecutionId, state.IncidentId);
            return new ValueTask<bool>(_states.TryAdd(key, state));
        }
    }

    public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new IncidentStateKey(state.WorkflowExecutionId, state.IncidentId);
            _states.TryGetValue(key, out var existing);
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, state);
            _states[key] = state;
            return new ValueTask<IncidentState>(state);
        }
    }

    public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(new IncidentStateKey(workflowExecutionId, incidentId), out var state);
            return new ValueTask<IncidentState?>(state);
        }
    }

    public ValueTask<int> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
            return new ValueTask<int>(_states.Keys.Count(key => key.WorkflowExecutionId == workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<IncidentState>>(ListByWorkflowExecution(workflowExecutionId));
        }
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .Where(state => state.IsBlocking)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<IncidentState>>(states);
        }
    }

    /// <summary>
    /// Returns one stable snapshot of every in-memory incident. Operational read models use this to evaluate
    /// the complete volatile store without issuing one query per workflow execution.
    /// </summary>
    public ValueTask<IReadOnlyCollection<IncidentState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
            return ValueTask.FromResult<IReadOnlyCollection<IncidentState>>(_states.Values.ToArray());
    }

    private IncidentState[] ListByWorkflowExecution(string workflowExecutionId) =>
        _states
            .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
            .Select(item => item.Value)
            .ToArray();

    private readonly record struct IncidentStateKey(string WorkflowExecutionId, string IncidentId);
}
