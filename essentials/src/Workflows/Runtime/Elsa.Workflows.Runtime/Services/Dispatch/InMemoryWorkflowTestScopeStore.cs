using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.TestScopes;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Dispatch;

/// <summary>Shared-fence in-memory test-scope lifecycle, admission, and detached cleanup provider.</summary>
public sealed class InMemoryWorkflowTestScopeStore :
    IWorkflowTestScopeStore,
    IWorkflowTestScopeAdmissionStore,
    IWorkflowTestScopeCleanupStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;
    private readonly InMemoryWorkflowTestScopeLifecycleStore _lifecycle;

    public InMemoryWorkflowTestScopeStore(InMemoryRuntimeCheckpointStoreState? state = null)
    {
        _state = state ?? new InMemoryRuntimeCheckpointStoreState();
        _lifecycle = new(_state.SyncRoot, _state.WriteGate, _state.WorkflowTestScopes);
    }

    public ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default) =>
        _lifecycle.CreateAsync(scope, createdAt, cancellationToken);

    public ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default) =>
        _lifecycle.FindAsync(scopeId, cancellationToken);

    public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default) =>
        _lifecycle.CloseAsync(request, cancellationToken);

    public ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        _lifecycle.CompleteAsync(scopeId, completedAt, cancellationToken);

    public ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default) =>
        _lifecycle.QueryAsync(query, cancellationToken);

    public ValueTask AssertOpenAsync(
        WorkflowTestScope scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            WorkflowTestScopeAdmission.EnsureOpen(_state.WorkflowTestScopes.GetValueOrDefault(scope.ScopeId), scope, observedAt);
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
            // Converge on the responsibility already recorded and leave the item exactly as it is: it may already be
            // claimed, delivered, or retried, and writing it back as pending would regress it.
            WorkflowDispatchLifecycle.EnsureTestScopeCancellationResponsibility(existing, intent);
            return;
        }

        _state.OutboxItems.Add(outboxItemId, item);
    }
}
