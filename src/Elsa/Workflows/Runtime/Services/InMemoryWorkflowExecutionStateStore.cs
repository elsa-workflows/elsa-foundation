using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutionStateStore() : InMemoryKeyedStateStore<string, WorkflowExecutionState>(StringComparer.Ordinal), IWorkflowExecutionStateStore
{
    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Save(state.WorkflowExecutionId, state));
    }

    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotAll());
    }
}
