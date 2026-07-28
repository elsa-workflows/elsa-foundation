using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>Restart-convergent Groundwork cleanup for detached test-scope dispatches.</summary>
public sealed class GroundworkTestScopeCleanupStore : IWorkflowTestScopeCleanupStore
{
    private readonly IWorkflowTestScopeStore _scopeStore;
    private readonly GroundworkWorkflowDispatchStore _dispatchStore;
    private readonly IDocumentStore _store;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;

    public GroundworkTestScopeCleanupStore(
        IWorkflowTestScopeStore scopeStore,
        GroundworkWorkflowDispatchStore dispatchStore,
        GroundworkRuntimePostCommitOutboxStore outboxStore)
    {
        ArgumentNullException.ThrowIfNull(scopeStore);
        ArgumentNullException.ThrowIfNull(dispatchStore);
        ArgumentNullException.ThrowIfNull(outboxStore);
        if (scopeStore is not GroundworkWorkflowTestScopeStore providerScopeStore ||
            !ReferenceEquals(providerScopeStore.DocumentStore, dispatchStore.DocumentStore) ||
            !ReferenceEquals(outboxStore.DocumentStore, dispatchStore.DocumentStore))
        {
            throw new InvalidOperationException(
                "Groundwork test-scope cleanup requires scope, dispatch, and outbox stores on one document provider.");
        }

        _scopeStore = scopeStore;
        _dispatchStore = dispatchStore;
        _store = dispatchStore.DocumentStore;
        _serializer = dispatchStore.Serializer;
        _accessContextAccessor = dispatchStore.AccessContextAccessor;
    }

    public async ValueTask<WorkflowTestScopeCleanupResult> CleanupAsync(
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
        if (pageSize is <= 0 or > WorkflowDispatchQuery.MaximumTake)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new InvalidOperationException(
                "Groundwork cannot atomically clean a workflow test scope because the active document store does not support cross-unit transactions.");
        }

        var lifecycle = await _scopeStore.FindAsync(scope.ScopeId, cancellationToken)
            ?? throw new InvalidOperationException("The workflow test scope was not found for cleanup.");
        if (!WorkflowTestScope.ContextEquals(lifecycle.Scope, scope) || lifecycle.State == WorkflowTestScopeState.Open)
            throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");

        var pending = await QueryActionableAsync(scope, WorkflowDispatchStatus.Pending, pageSize, cancellationToken);
        var started = await QueryActionableAsync(scope, WorkflowDispatchStatus.Started, pageSize, cancellationToken);
        var page = pending
            .Concat(started)
            .Where(record => continuationToken is null ||
                             StringComparer.Ordinal.Compare(record.DispatchId, continuationToken) > 0)
            .Where(record => record.Mode == WorkflowDispatchMode.FireAndForget)
            .OrderBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(pageSize)
            .ToArray();
        var cancelledBeforeAdmission = 0;
        var cancellationQueued = 0;
        var terminalUnchanged = 0;

        if (page.Length > 0)
        {
            await using var unitOfWork = await _store.BeginAsync(
                DocumentCommitScope.Of(
                    ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
                    ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind),
                cancellationToken);
            var transactionalStore = new GroundworkDocumentUnitOfWorkStore(_store, unitOfWork);
            var transactionalScope = new GroundworkWorkflowTestScopeStore(
                transactionalStore,
                _serializer,
                _accessContextAccessor);
            var transactionalDispatch = new GroundworkWorkflowDispatchStore(
                transactionalStore,
                _serializer,
                _accessContextAccessor);
            var transactionalOutbox = new GroundworkRuntimePostCommitOutboxStore(
                transactionalStore,
                _serializer,
                accessContextAccessor: _accessContextAccessor);

            // The scope touch and every mutation in this bounded page share one commit. Admission and
            // cleanup cannot both win a Pending dispatch, and a Started marker cannot escape without
            // its deterministic cancellation outbox responsibility.
            await transactionalScope.AssertClosingAsync(scope, cancellationToken);
            foreach (var candidate in page)
            {
                var record = await transactionalDispatch.FindAsync(candidate.DispatchId, cancellationToken);
                if (record is null || !WorkflowTestScope.ContextEquals(record.TestScope, scope) ||
                    record.Mode != WorkflowDispatchMode.FireAndForget)
                {
                    continue;
                }

                if (record.Status == WorkflowDispatchStatus.Pending)
                {
                    await transactionalDispatch.SaveAsync(
                        WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(record, requestedAt),
                        cancellationToken);
                    cancelledBeforeAdmission++;
                    continue;
                }

                if (record.Status == WorkflowDispatchStatus.Started)
                {
                    if (!cancellationIntents.TryGetValue(record.DispatchId, out var intent))
                        throw new InvalidOperationException("A started test-scope dispatch requires deterministic cancellation responsibility.");
                    WorkflowDispatchLifecycle.ValidateTestScopeCancellationIntent(record, intent);
                    var marked = WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record)
                        ? record
                        : WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(record, requestedAt);
                    await transactionalDispatch.SaveAsync(marked, cancellationToken);
                    var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
                    await transactionalOutbox.SavePendingAsync(
                        new RuntimePostCommitOutboxItem(
                            identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}"),
                            intent,
                            RuntimePostCommitOutboxStatus.Pending,
                            requestedAt,
                            requestedAt,
                            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))),
                        cancellationToken);
                    cancellationQueued++;
                    continue;
                }

                terminalUnchanged++;
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }

        var remaining = await CountLiveAsync(scope, WorkflowDispatchStatus.Pending, cancellationToken) +
                        await CountLiveAsync(scope, WorkflowDispatchStatus.Started, cancellationToken);
        return new WorkflowTestScopeCleanupResult(
            page.Length,
            cancelledBeforeAdmission,
            cancellationQueued,
            terminalUnchanged,
            remaining);
    }

    private async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryActionableAsync(
        WorkflowTestScope scope,
        WorkflowDispatchStatus status,
        int take,
        CancellationToken cancellationToken)
    {
        var actionable = new List<WorkflowDispatchRecord>(take);
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (actionable.Count < take)
        {
            var page = await _dispatchStore.QueryAsync(
                new WorkflowDispatchQuery(
                    status: status,
                    take: WorkflowDispatchQuery.MaximumTake,
                    afterCreatedAt: afterCreatedAt,
                    afterDispatchId: afterDispatchId,
                    testScopeId: scope.ScopeId),
                cancellationToken);
            if (page.Count == 0)
                break;
            actionable.AddRange(page.Where(record =>
                record.Mode == WorkflowDispatchMode.FireAndForget &&
                (status != WorkflowDispatchStatus.Started ||
                 !WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record))));
            if (page.Count < WorkflowDispatchQuery.MaximumTake)
                break;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }

        return actionable.Take(take).ToArray();
    }

    private async ValueTask<int> CountLiveAsync(
        WorkflowTestScope scope,
        WorkflowDispatchStatus status,
        CancellationToken cancellationToken)
    {
        var count = 0;
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (true)
        {
            var page = await _dispatchStore.QueryAsync(
                new WorkflowDispatchQuery(
                    status: status,
                    take: WorkflowDispatchQuery.MaximumTake,
                    afterCreatedAt: afterCreatedAt,
                    afterDispatchId: afterDispatchId,
                    testScopeId: scope.ScopeId),
                cancellationToken);
            count += page.Count(record => record.Mode == WorkflowDispatchMode.FireAndForget);
            if (page.Count < WorkflowDispatchQuery.MaximumTake)
                return count;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }
    }

}
