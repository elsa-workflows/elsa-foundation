using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable tenant-scoped workflow-dispatch lifecycle store. Immutable context and lifecycle transitions
/// use the runtime-owned validator; optimistic document versions fence concurrent projections.
/// </summary>
public sealed class GroundworkWorkflowDispatchStore :
    IWorkflowDispatchStore,
    IWorkflowDispatchQueryStore,
    IWorkflowDispatchDeleteStore,
    IWorkflowDispatchRetentionRootStore,
    IWorkflowDispatchAdmissionStore,
    IWorkflowDispatchCancellationStore
{
    private const int CandidatePageSize = WorkflowDispatchQuery.MaximumTake;
    private readonly IDocumentStore _store;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IBoundedDocumentStore? _boundedStore;
    private readonly IWorkflowTestScopeAdmissionStore? _testScopeAdmissionStore;

    internal IDocumentStore DocumentStore => _store;
    internal IGroundworkRuntimeDocumentSerializer Serializer => _serializer;
    internal IPersistenceAccessContextAccessor AccessContextAccessor => _accessContextAccessor;

    public GroundworkWorkflowDispatchStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IBoundedDocumentStore? boundedStore = null,
        IWorkflowTestScopeAdmissionStore? testScopeAdmissionStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
        _testScopeAdmissionStore = testScopeAdmissionStore;
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore ?? throw new InvalidOperationException(
        "Workflow-dispatch queries require an admitted bounded document-store runtime.");

    public async ValueTask<WorkflowDispatchRecord> SaveAsync(
        WorkflowDispatchRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        _accessContextAccessor.Current.EnsureTenantScope(record.TenantId);

        while (true)
        {
            var existing = await LoadAsync(record.DispatchId, cancellationToken);
            if (existing is null)
                WorkflowDispatchLifecycle.ValidateNew(record);
            else
            {
                WorkflowDispatchLifecycle.ValidateTransition(existing.Record, record);
                if (WorkflowDispatchLifecycle.RecordsEqual(existing.Record, record))
                    return existing.Record;
            }

            var result = await SaveAsync(record, existing?.Version ?? 0, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return record;
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow dispatch '{record.DispatchId}' with status '{result.Status}'.");
            }

            // A racing equivalent write is idempotent. A conflicting identity, lifecycle regression, or
            // later terminal state is rejected by the shared validator on the next loop iteration.
        }
    }

    public async ValueTask<WorkflowDispatchRecord?> FindAsync(
        string dispatchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        var record = (await LoadAsync(dispatchId, cancellationToken))?.Record;
        if (record is not null)
            _accessContextAccessor.Current.EnsureTenantScope(record.TenantId);
        return record;
    }

    internal async ValueTask<bool> TrySaveRedriveAsync(
        WorkflowDispatchRecord expected,
        WorkflowDispatchRecord candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        _accessContextAccessor.Current.EnsureTenantScope(expected.TenantId);

        var loaded = await LoadAsync(expected.DispatchId, cancellationToken);
        if (loaded is null || !WorkflowDispatchLifecycle.RecordsEqual(loaded.Record, expected))
            return false;
        var requestId = WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(candidate)
            ?? throw new InvalidOperationException("A sanctioned workflow-dispatch redrive requires its operator request identity.");
        var expectedCandidate = WorkflowDispatchLifecycle.RedriveDelivery(expected, requestId, candidate.UpdatedAt);
        if (!WorkflowDispatchLifecycle.RecordsEqual(expectedCandidate, candidate))
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' carries a conflicting redrive transition.");

        var result = await SaveAsync(candidate, loaded.Version, cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return true;
        if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            return false;
        throw new InvalidOperationException(
            $"Groundwork rejected workflow dispatch redrive '{candidate.DispatchId}' with status '{result.Status}'.");
    }

    public async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (admittedAt == default)
            throw new ArgumentOutOfRangeException(nameof(admittedAt), "Child admission requires a recorded timestamp.");
        cancellationToken.ThrowIfCancellationRequested();

        var initial = await LoadAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
        _accessContextAccessor.Current.EnsureTenantScope(initial.Record.TenantId);
        if (initial.Record.TestScope is not null && initial.Record.Mode == WorkflowDispatchMode.FireAndForget)
            return await TryAdmitTestScopedAsync(dispatchId, admittedAt, cancellationToken);

        while (true)
        {
            var loaded = await LoadAsync(dispatchId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Record.TenantId);
            if (loaded.Record.Status == WorkflowDispatchStatus.Started)
                return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, loaded.Record);
            if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(loaded.Record))
                return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, loaded.Record);
            if (loaded.Record.Status != WorkflowDispatchStatus.Pending)
                return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.Terminal, loaded.Record);

            var effectiveAdmittedAt = admittedAt > loaded.Record.UpdatedAt
                ? admittedAt
                : loaded.Record.UpdatedAt;
            var admitted = loaded.Record.TransitionTo(WorkflowDispatchStatus.Started, effectiveAdmittedAt);
            var result = await SaveAsync(admitted, loaded.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.Admitted, admitted);
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork rejected workflow dispatch admission '{dispatchId}' with status '{result.Status}'.");
        }
    }

    private async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitTestScopedAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken)
    {
        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new InvalidOperationException(
                "Groundwork cannot atomically admit a test-scoped workflow dispatch because the active document store does not support cross-unit transactions.");
        }
        if (_testScopeAdmissionStore is not GroundworkWorkflowTestScopeStore providerScopeStore ||
            !ReferenceEquals(providerScopeStore.DocumentStore, _store))
        {
            throw new InvalidOperationException(
                "Scoped child admission requires the Groundwork workflow test-scope provider on the same document store.");
        }

        await using var unitOfWork = await _store.BeginAsync(
            DocumentCommitScope.Of(
                ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind),
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
        var record = await transactionalDispatch.FindAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
        _accessContextAccessor.Current.EnsureTenantScope(record.TenantId);

        if (record.Status == WorkflowDispatchStatus.Started)
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, record);
        if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(record))
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.Terminal, record);

        var testScope = record.TestScope!;
        WorkflowDispatchRecord candidate;
        WorkflowDispatchAdmissionDisposition disposition;
        try
        {
            // AssertOpenAsync performs a same-value CAS touch. The scope touch and Pending -> Started
            // transition therefore commit together and serialize with scope close/cleanup.
            await transactionalScope.AssertOpenAsync(testScope, admittedAt, cancellationToken);
            var effectiveAdmittedAt = admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt;
            candidate = record.TransitionTo(WorkflowDispatchStatus.Started, effectiveAdmittedAt);
            disposition = WorkflowDispatchAdmissionDisposition.Admitted;
        }
        catch (TestScopeAdmissionException)
        {
            var effectiveAdmittedAt = admittedAt > record.UpdatedAt ? admittedAt : record.UpdatedAt;
            candidate = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(record, effectiveAdmittedAt);
            disposition = WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission;
        }

        await transactionalDispatch.SaveAsync(candidate, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return new WorkflowDispatchAdmissionResult(disposition, candidate);
    }

    public async ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(
        WorkflowDispatchCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            var loaded = await LoadAsync(request.DispatchId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Record.TenantId);
            ValidateCancellationIdentity(loaded.Record, request);

            WorkflowDispatchRecord candidate;
            WorkflowDispatchCancellationDisposition disposition;
            if (loaded.Record.Status == WorkflowDispatchStatus.Pending)
            {
                candidate = WorkflowDispatchLifecycle.CancelBeforeAdmission(loaded.Record, request.RequestedAt);
                disposition = WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission;
            }
            else if (loaded.Record.Status == WorkflowDispatchStatus.Started)
            {
                candidate = WorkflowDispatchLifecycle.MarkCancellationRequested(loaded.Record, request.RequestedAt);
                disposition = WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission;
            }
            else
            {
                return new WorkflowDispatchCancellationResult(
                    WorkflowDispatchCancellationDisposition.TerminalUnchanged,
                    loaded.Record);
            }

            if (WorkflowDispatchLifecycle.RecordsEqual(loaded.Record, candidate))
                return new WorkflowDispatchCancellationResult(disposition, loaded.Record);

            var result = await SaveAsync(candidate, loaded.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return new WorkflowDispatchCancellationResult(disposition, candidate);
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork rejected workflow dispatch cancellation '{request.DispatchId}' with status '{result.Status}'.");
        }
    }

    public async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
        string parentWorkflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        return await QueryAsync(
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentQuery,
            [Equal(ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField, parentWorkflowExecutionId)],
            take: null,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var (identity, clauses) = SelectRoute(query);
        // Every provider read is bounded. Continue through declared offset pages so predicates not carried by
        // the selected index are still applied before the public Take, while retaining only the best bounded
        // result set in memory.
        var selected = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        var skip = 0;
        while (true)
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                    identity,
                    clauses,
                    skip: skip,
                    take: CandidatePageSize),
                cancellationToken);
            var documents = result.Documents;
            foreach (var record in documents
                         .Select(_serializer.Deserialize<WorkflowDispatchDocument>)
                         .Select(document => document.Record)
                         .Where(record => Matches(record, query)))
            {
                if (selected.TryGetValue(record.DispatchId, out var duplicate) &&
                    !WorkflowDispatchLifecycle.RecordsEqual(duplicate, record))
                {
                    throw new InvalidOperationException(
                        $"Groundwork returned conflicting workflow dispatch documents for '{record.DispatchId}'.");
                }

                selected[record.DispatchId] = record;
            }

            if (selected.Count > query.Take)
            {
                selected = selected.Values
                    .OrderBy(record => record.CreatedAt)
                    .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
                    .Take(query.Take)
                    .ToDictionary(record => record.DispatchId, StringComparer.Ordinal);
            }

            if (documents.Count < CandidatePageSize)
                break;
            skip += documents.Count;
        }

        var records = selected.Values
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(query.Take)
            .ToArray();
        foreach (var record in records)
            _accessContextAccessor.Current.EnsureTenantScope(record.TenantId);
        return records;
    }

    public async ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();

        var loaded = await LoadAsync(dispatchId, cancellationToken);
        if (loaded is null)
            return;

        var result = await _store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(dispatchId),
                loaded.Version),
            cancellationToken);
        if (result.Status is DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound)
            return;
        throw new InvalidOperationException(
            $"Groundwork rejected deletion of workflow dispatch '{dispatchId}' with status '{result.Status}'.");
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await QueryAsync(
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesQuery,
            [Equal(ElsaRuntimeStorageManifest.CollectionField, ElsaRuntimeStorageManifest.WorkflowDispatchCollection)],
            take: null,
            cancellationToken);

        return records
            .Where(record => record.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
            .Select(record => record.ChildExecutable.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<LoadedDispatch?> LoadAsync(string dispatchId, CancellationToken cancellationToken)
    {
        var envelope = await _store.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
            GroundworkPhysicalDocumentId.FromLogicalId(dispatchId),
            cancellationToken);
        if (envelope is null)
            return null;

        var document = _serializer.Deserialize<WorkflowDispatchDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.Record.DispatchId, dispatchId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for workflow dispatch '{dispatchId}'.");
        }

        return new LoadedDispatch(document.Record, envelope.Version);
    }

    private Task<DocumentStoreWriteResult> SaveAsync(
        WorkflowDispatchRecord record,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var document = WorkflowDispatchDocument.From(record);
        var (schemaVersion, content) = _serializer.Serialize(
            ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
            document);
        return _store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(record.DispatchId),
                schemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private static void ValidateCancellationIdentity(
        WorkflowDispatchRecord record,
        WorkflowDispatchCancellationRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.DispatchId, request.DispatchId) ||
            !StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId) ||
            record.Mode != WorkflowDispatchMode.WaitForCompletion ||
            !WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(record))
        {
            throw new InvalidOperationException($"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with its durable dispatch record.");
        }
    }

    private async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        int? take,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                queryIdentity,
                clauses,
                take: take),
            cancellationToken);
        return result.Documents
            .Select(_serializer.Deserialize<WorkflowDispatchDocument>)
            .Select(document => document.Record)
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .ToArray();
    }

    private static (string QueryIdentity, IReadOnlyList<DocumentQueryClause> Clauses) SelectRoute(
        WorkflowDispatchQuery query)
    {
        var status = query.Status?.ToString();

        // A child identity deterministically identifies one dispatch, so it remains the narrowest route
        // when the caller also supplies parent context. Status intersections use the declared composite.
        if (query.ChildWorkflowExecutionId is { } child)
        {
            return status is null
                ? (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildQuery,
                    [Equal(ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField, child)])
                : (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildAndStatusQuery,
                    [
                        Equal(ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField, child),
                        Equal(ElsaRuntimeStorageManifest.StatusField, status)
                    ]);
        }

        if (query.ParentWorkflowExecutionId is { } parent)
        {
            return status is null
                ? (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentQuery,
                    [Equal(ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField, parent)])
                : (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentAndStatusQuery,
                    [
                        Equal(ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField, parent),
                        Equal(ElsaRuntimeStorageManifest.StatusField, status)
                    ]);
        }

        if (query.TestScopeId is { } testScopeId)
        {
            return (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery,
                [Equal(ElsaRuntimeStorageManifest.TestScopeIdField, testScopeId)]);
        }

        return (ElsaRuntimeStorageManifest.ListWorkflowDispatchesByStatusQuery,
            [Equal(ElsaRuntimeStorageManifest.StatusField, status!)]);
    }

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static bool Matches(WorkflowDispatchRecord record, WorkflowDispatchQuery query) =>
        (query.ParentWorkflowExecutionId is null ||
         StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, query.ParentWorkflowExecutionId)) &&
        (query.ChildWorkflowExecutionId is null ||
         StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, query.ChildWorkflowExecutionId)) &&
        (query.Status is null || record.Status == query.Status) &&
        (query.TestScopeId is null || StringComparer.Ordinal.Equals(record.TestScope?.ScopeId, query.TestScopeId)) &&
        (query.AfterCreatedAt is null ||
         record.CreatedAt > query.AfterCreatedAt ||
         record.CreatedAt == query.AfterCreatedAt &&
         StringComparer.Ordinal.Compare(record.DispatchId, query.AfterDispatchId) > 0);

    private sealed record LoadedDispatch(WorkflowDispatchRecord Record, long Version);
}

internal sealed record WorkflowDispatchDocument(
    string Collection,
    string ParentWorkflowExecutionId,
    string ChildWorkflowExecutionId,
    string Status,
    string? TestScopeId,
    string? TenantId,
    WorkflowDispatchRecord Record)
{
    public static WorkflowDispatchDocument From(WorkflowDispatchRecord record) => new(
        ElsaRuntimeStorageManifest.WorkflowDispatchCollection,
        record.ParentWorkflowExecutionId,
        record.ChildWorkflowExecutionId,
        record.Status.ToString(),
        record.TestScope?.ScopeId,
        record.TenantId,
        record);
}
