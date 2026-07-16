using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide in-memory workflow-dispatch projection.</summary>
public sealed class InMemoryWorkflowDispatchStore : IWorkflowDispatchStore, IWorkflowDispatchQueryStore, IWorkflowDispatchDeleteStore, IWorkflowDispatchRetentionRootStore
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
            if (_state.WorkflowDispatches.TryGetValue(record.DispatchId, out var existing))
                WorkflowDispatchLifecycle.ValidateTransition(existing, record);
            else
                WorkflowDispatchLifecycle.ValidateNew(record);
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

    public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var records = _state.WorkflowDispatches.Values
                .Where(record => query.ParentWorkflowExecutionId is null ||
                    StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, query.ParentWorkflowExecutionId))
                .Where(record => query.ChildWorkflowExecutionId is null ||
                    StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, query.ChildWorkflowExecutionId))
                .Where(record => query.Status is null || record.Status == query.Status)
                .Where(record => query.AfterCreatedAt is null ||
                    record.CreatedAt > query.AfterCreatedAt ||
                    record.CreatedAt == query.AfterCreatedAt &&
                    StringComparer.Ordinal.Compare(record.DispatchId, query.AfterDispatchId) > 0)
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
                .Take(query.Take)
                .ToArray();
            return new ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>>(records);
        }
    }

    public ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
            _state.WorkflowDispatches.Remove(dispatchId);

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            IReadOnlyCollection<string> artifactIds = _state.WorkflowDispatches.Values
                .Where(record => record.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
                .Select(record => record.ChildExecutable.ArtifactId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(artifactIds);
        }
    }
}
