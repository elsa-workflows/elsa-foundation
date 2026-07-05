using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowHoldStateStore : IWorkflowHoldStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WorkflowHoldState> _states = new(StringComparer.Ordinal);

    public ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states[state.ControlPlaneStateId] = state;
            return new ValueTask<WorkflowHoldState>(state);
        }
    }

    public ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(controlPlaneStateId, out var state);
            return new ValueTask<WorkflowHoldState?>(state);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states.Values
                .Where(state =>
                    StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
                    HasWorkflowScopedHold(state, workflowExecutionId))
                .ToArray();

            return new ValueTask<IReadOnlyCollection<WorkflowHoldState>>(states);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<WorkflowHoldState>>(_states.Values.ToArray());
        }
    }

    private static bool HasWorkflowScopedHold(WorkflowHoldState state, string workflowExecutionId) =>
        state.ActiveHolds.Concat(state.ReleasedHolds)
            .Any(hold => StringComparer.Ordinal.Equals(hold.WorkflowExecutionId, workflowExecutionId));
}
