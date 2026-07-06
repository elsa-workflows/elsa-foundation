using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryExecutionLivenessStateStore : InMemoryKeyedStateStore<InMemoryExecutionLivenessStateStore.ExecutionLivenessStateKey, ExecutionLivenessState>, IExecutionLivenessStateStore
{
    public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new ExecutionLivenessStateKey(state.WorkflowExecutionId, state.OperationalStateId);
        return new(Save(key, state));
    }

    public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(new ExecutionLivenessStateKey(workflowExecutionId, operationalStateId)));
    }

    public ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Snapshot(key => key.WorkflowExecutionId == workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotAll());
    }

    public readonly record struct ExecutionLivenessStateKey(string WorkflowExecutionId, string OperationalStateId);
}
