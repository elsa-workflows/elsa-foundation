using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide in-memory workflow-dispatch projection.</summary>
public sealed class InMemoryWorkflowDispatchStore : IWorkflowDispatchStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;

    public InMemoryWorkflowDispatchStore(InMemoryRuntimeCheckpointStoreState? state = null) =>
        _state = state ?? new InMemoryRuntimeCheckpointStoreState();

    public ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            _state.WorkflowDispatches[record.DispatchId] = record;
            return new ValueTask<WorkflowDispatchRecord>(record);
        }
    }

    public ValueTask<WorkflowDispatchRecord?> FindAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            _state.WorkflowDispatches.TryGetValue(dispatchId, out var record);
            return new ValueTask<WorkflowDispatchRecord?>(record);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
        string parentWorkflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var records = _state.WorkflowDispatches.Values
                .Where(record => StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, parentWorkflowExecutionId))
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
                .ToArray();
            return new ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>>(records);
        }
    }
}
