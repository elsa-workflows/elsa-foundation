using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Shared-fence in-memory test-scope lifecycle, admission, and detached cleanup provider.</summary>
public sealed class InMemoryWorkflowTestScopeStore :
    IWorkflowTestScopeStore,
    IWorkflowTestScopeAdmissionStore,
    IWorkflowTestScopeCleanupStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;

    public InMemoryWorkflowTestScopeStore(InMemoryRuntimeCheckpointStoreState? state = null) =>
        _state = state ?? new InMemoryRuntimeCheckpointStoreState();

    public ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            _state.WorkflowTestScopes.TryGetValue(scope.ScopeId, out var existing);
            var record = WorkflowTestScopeTransitions.Create(existing, scope, createdAt);
            _state.WorkflowTestScopes[scope.ScopeId] = record;
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            _state.WorkflowTestScopes.TryGetValue(scopeId, out var record);
            return ValueTask.FromResult(record);
        }
    }

    public async ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var checkpointGate = await _state.TransactionGate.EnterAsync(cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            lock (_state.SyncRoot)
            {
                _state.WorkflowTestScopes.TryGetValue(request.ScopeId, out var existing);
                var result = WorkflowTestScopeTransitions.Close(existing, request);
                if (result.Record is not null)
                    _state.WorkflowTestScopes[request.ScopeId] = result.Record;
                return result;
            }
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    public ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowTestScopes.TryGetValue(scopeId, out var existing))
                throw new InvalidOperationException("The workflow test scope was not found for closure completion.");
            var record = WorkflowTestScopeTransitions.Complete(existing, completedAt);
            _state.WorkflowTestScopes[scopeId] = record;
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var candidates = _state.WorkflowTestScopes.Values
                .Where(record => query.State is { } state
                    ? record.State == state
                    : record.State == WorkflowTestScopeState.Closing ||
                      record.State == WorkflowTestScopeState.Open && record.Scope.IsExpired(query.ObservedAt))
                .Where(record => query.ContinuationToken is null ||
                    StringComparer.Ordinal.Compare(record.Scope.ScopeId, query.ContinuationToken) > 0)
                .OrderBy(record => record.Scope.ScopeId, StringComparer.Ordinal)
                .Take(query.PageSize + 1)
                .ToArray();
            var items = candidates.Take(query.PageSize).ToArray();
            var continuation = candidates.Length > query.PageSize ? items[^1].Scope.ScopeId : null;
            return ValueTask.FromResult(new WorkflowTestScopePage(items, continuation));
        }
    }

    public ValueTask AssertOpenAsync(
        WorkflowTestScope scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowTestScopes.TryGetValue(scope.ScopeId, out var record) ||
                record.State != WorkflowTestScopeState.Open ||
                record.Scope.IsExpired(observedAt) ||
                !WorkflowTestScope.ContextEquals(record.Scope, scope))
            {
                throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<WorkflowTestScopeCleanupResult> CleanupAsync(
        WorkflowTestScope scope,
        DateTimeOffset requestedAt,
        int pageSize,
        IReadOnlyDictionary<string, RuntimePostCommitIntent> cancellationIntents,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(cancellationIntents);
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (continuationToken is not null && string.IsNullOrWhiteSpace(continuationToken))
            throw new ArgumentException("The cleanup continuation token cannot be blank.", nameof(continuationToken));
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            if (!_state.WorkflowTestScopes.TryGetValue(scope.ScopeId, out var scopeRecord) ||
                scopeRecord.State == WorkflowTestScopeState.Open ||
                !WorkflowTestScope.ContextEquals(scopeRecord.Scope, scope))
            {
                throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");
            }

            var candidates = _state.WorkflowDispatches.Values
                .Where(record => WorkflowTestScope.ContextEquals(record.TestScope, scope))
                .Where(record => record.Mode == WorkflowDispatchMode.FireAndForget)
                .Where(record => record.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
                .Where(record => record.Status != WorkflowDispatchStatus.Started ||
                                 !WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record))
                .Where(record => continuationToken is null || StringComparer.Ordinal.Compare(record.DispatchId, continuationToken) > 0)
                .OrderBy(record => record.DispatchId, StringComparer.Ordinal)
                .Take(pageSize + 1)
                .ToArray();
            var page = candidates.Take(pageSize).ToArray();
            var cancelledBeforeAdmission = 0;
            var cancellationQueued = 0;
            var terminalUnchanged = 0;

            foreach (var record in page)
            {
                switch (record.Status)
                {
                    case WorkflowDispatchStatus.Pending:
                        _state.WorkflowDispatches[record.DispatchId] =
                            WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(record, requestedAt);
                        cancelledBeforeAdmission++;
                        break;
                    case WorkflowDispatchStatus.Started:
                        if (!cancellationIntents.TryGetValue(record.DispatchId, out var intent))
                            throw new InvalidOperationException("A started test-scope dispatch requires deterministic cancellation responsibility.");
                WorkflowDispatchLifecycle.ValidateTestScopeCancellationIntent(record, intent);
                        var marked = WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record)
                            ? record
                            : WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(record, requestedAt);
                        _state.WorkflowDispatches[record.DispatchId] = marked;
                        SaveCancellationOutbox(scope, marked, intent, requestedAt);
                        cancellationQueued++;
                        break;
                    default:
                        terminalUnchanged++;
                        break;
                }
            }

            var remainingLive = _state.WorkflowDispatches.Values.Count(record =>
                WorkflowTestScope.ContextEquals(record.TestScope, scope) &&
                record.Mode == WorkflowDispatchMode.FireAndForget &&
                record.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started);
            var next = candidates.Length > pageSize ? page[^1].DispatchId : null;
            return ValueTask.FromResult(new WorkflowTestScopeCleanupResult(
                page.Length,
                cancelledBeforeAdmission,
                cancellationQueued,
                terminalUnchanged,
                remainingLive,
                next));
        }
    }

    private void SaveCancellationOutbox(
        WorkflowTestScope scope,
        WorkflowDispatchRecord record,
        RuntimePostCommitIntent intent,
        DateTimeOffset requestedAt)
    {
        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        var outboxItemId = identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        var item = new RuntimePostCommitOutboxItem(
            outboxItemId,
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            requestedAt,
            requestedAt,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        if (_state.OutboxItems.TryGetValue(outboxItemId, out var existing))
        {
            if (!Equivalent(existing.Intent, intent))
                throw new InvalidOperationException("The workflow test-scope cancellation outbox item conflicts with committed responsibility.");
            return;
        }

        _state.OutboxItems.Add(outboxItemId, item);
    }

    private static bool Equivalent(RuntimePostCommitIntent left, RuntimePostCommitIntent right) =>
        StringComparer.Ordinal.Equals(left.IntentId, right.IntentId) &&
        StringComparer.Ordinal.Equals(left.WorkflowExecutionId, right.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        left.RecordedAt == right.RecordedAt &&
        StringComparer.Ordinal.Equals(left.ActivityExecutionId, right.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.IdempotencyKey, right.IdempotencyKey) &&
        Nullable.Equals(left.Payload, right.Payload) &&
        left.Metadata.Count == right.Metadata.Count &&
        left.Metadata.All(item => right.Metadata.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
}
