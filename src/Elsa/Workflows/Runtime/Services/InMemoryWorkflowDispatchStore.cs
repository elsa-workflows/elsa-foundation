using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide in-memory workflow-dispatch projection.</summary>
public sealed class InMemoryWorkflowDispatchStore : IWorkflowDispatchStore, IWorkflowDispatchQueryStore, IWorkflowDispatchDeleteStore, IWorkflowDispatchRetentionRootStore, IWorkflowDispatchAdmissionStore, IWorkflowDispatchCancellationStore
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

    public ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (admittedAt == default)
            throw new ArgumentOutOfRangeException(nameof(admittedAt), "Child admission requires a recorded timestamp.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowDispatches.TryGetValue(dispatchId, out var record))
                throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");

            if (record.Status == WorkflowDispatchStatus.Pending)
            {
                if (record is { Mode: WorkflowDispatchMode.FireAndForget, TestScope: { } testScope } &&
                    (!_state.WorkflowTestScopes.TryGetValue(testScope.ScopeId, out var scopeRecord) ||
                     scopeRecord.State != WorkflowTestScopeState.Open ||
                     scopeRecord.Scope.IsExpired(admittedAt) ||
                     !WorkflowTestScope.ContextEquals(scopeRecord.Scope, testScope)))
                {
                    var effectiveAdmittedAt = admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt;
                    var cancelled = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(record, effectiveAdmittedAt);
                    _state.WorkflowDispatches[dispatchId] = cancelled;
                    return ValueTask.FromResult(new WorkflowDispatchAdmissionResult(
                        WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission,
                        cancelled));
                }

                var admitted = record.TransitionTo(
                    WorkflowDispatchStatus.Started,
                    admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt);
                _state.WorkflowDispatches[dispatchId] = admitted;
                return ValueTask.FromResult(new WorkflowDispatchAdmissionResult(
                    WorkflowDispatchAdmissionDisposition.Admitted,
                    admitted));
            }

            var disposition = record.Status switch
            {
                WorkflowDispatchStatus.Started => WorkflowDispatchAdmissionDisposition.AlreadyAdmitted,
                WorkflowDispatchStatus.Cancelled when WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(record) =>
                    WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission,
                _ => WorkflowDispatchAdmissionDisposition.Terminal
            };
            return ValueTask.FromResult(new WorkflowDispatchAdmissionResult(disposition, record));
        }
    }

    public ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(
        WorkflowDispatchCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowDispatches.TryGetValue(request.DispatchId, out var record))
                throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            ValidateCancellationRequest(record, request);
            if (!WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(record))
                throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' does not permit parent cancellation propagation.");

            WorkflowDispatchRecord resolved;
            WorkflowDispatchCancellationDisposition disposition;
            switch (record.Status)
            {
                case WorkflowDispatchStatus.Pending:
                    resolved = WorkflowDispatchLifecycle.CancelBeforeAdmission(record, request.RequestedAt);
                    disposition = WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission;
                    _state.WorkflowDispatches[record.DispatchId] = resolved;
                    break;
                case WorkflowDispatchStatus.Started:
                    resolved = WorkflowDispatchLifecycle.IsCancellationRequested(record)
                        ? record
                        : WorkflowDispatchLifecycle.MarkCancellationRequested(record, request.RequestedAt);
                    disposition = WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission;
                    _state.WorkflowDispatches[record.DispatchId] = resolved;
                    break;
                default:
                    resolved = record;
                    disposition = WorkflowDispatchCancellationDisposition.TerminalUnchanged;
                    break;
            }

            return ValueTask.FromResult(new WorkflowDispatchCancellationResult(disposition, resolved));
        }
    }

    private static void ValidateCancellationRequest(
        WorkflowDispatchRecord record,
        WorkflowDispatchCancellationRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException(
                $"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with the persisted dispatch identity.");
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
                .Where(record => query.TestScopeId is null ||
                    StringComparer.Ordinal.Equals(record.TestScope?.ScopeId, query.TestScopeId))
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

    public ValueTask<bool> TryDeleteAsync(
        WorkflowDispatchRecord expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!WorkflowDispatchLifecycle.IsTerminal(expected.Status))
            return ValueTask.FromResult(false);

        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowDispatches.TryGetValue(expected.DispatchId, out var current))
                return ValueTask.FromResult(true);
            if (!WorkflowDispatchLifecycle.RecordsEqual(current, expected) ||
                !WorkflowDispatchLifecycle.IsTerminal(current.Status))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(_state.WorkflowDispatches.Remove(expected.DispatchId));
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
