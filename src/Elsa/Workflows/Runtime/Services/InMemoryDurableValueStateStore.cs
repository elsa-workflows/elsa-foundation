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

    public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
        DurableValueStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var states = Snapshot(key => key.WorkflowExecutionId == query.WorkflowExecutionId)
            .OrderBy(state => state.DurableValueId, StringComparer.Ordinal)
            .ToArray();
        var queryBinding = $"durable-value-state:workflow:{query.WorkflowExecutionId}";
        var lastDurableValueId = InMemoryRuntimeStoreContinuation.Decode(
            query.ContinuationToken,
            queryBinding,
            nameof(query));
        var remaining = lastDurableValueId is null
            ? states
            : states.Where(state => StringComparer.Ordinal.Compare(state.DurableValueId, lastDurableValueId) > 0).ToArray();
        var items = remaining.Take(query.Limit).ToArray();
        var nextContinuationToken = remaining.Length > items.Length
            ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].DurableValueId)
            : null;

        return new(new RuntimeStorePage<DurableValueState>(query, items, nextContinuationToken));
    }

    public readonly record struct DurableValueStateKey(string WorkflowExecutionId, string DurableValueId);
}
