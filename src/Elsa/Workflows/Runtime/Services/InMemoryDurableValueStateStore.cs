using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryDurableValueStateStore : InMemoryKeyedStateStore<InMemoryDurableValueStateStore.DurableValueStateKey, DurableValueState>, IDurableValueStateStore
{
    public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DurableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new DurableValueStateKey(state.WorkflowExecutionId, state.DurableValueId);
        return new(Save(key, state));
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Remove(new DurableValueStateKey(workflowExecutionId, durableValueId)));
    }

    public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(new DurableValueStateKey(workflowExecutionId, durableValueId)));
    }

    public ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Snapshot(key => key.WorkflowExecutionId == workflowExecutionId));
    }

    public readonly record struct DurableValueStateKey(string WorkflowExecutionId, string DurableValueId);
}
