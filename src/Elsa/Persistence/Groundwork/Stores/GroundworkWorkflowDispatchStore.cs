using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable tenant-scoped workflow-dispatch lifecycle store. Immutable context and lifecycle transitions
/// use the runtime-owned validator; optimistic document versions fence concurrent projections.
/// </summary>
public sealed class GroundworkWorkflowDispatchStore :
    IWorkflowDispatchStore,
    IWorkflowDispatchQueryStore,
    IWorkflowDispatchDeleteStore,
    IWorkflowDispatchRetentionRootStore
{
    private const int CandidatePageSize = WorkflowDispatchQuery.MaximumTake;
    private readonly IDocumentStore _store;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IBoundedDocumentStore? _boundedStore;

    public GroundworkWorkflowDispatchStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IBoundedDocumentStore? boundedStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
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
        return (await LoadAsync(dispatchId, cancellationToken))?.Record;
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
        var selected = new List<WorkflowDispatchRecord>(query.Take + CandidatePageSize);
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
                selected.Add(record);
            }

            if (selected.Count > query.Take)
            {
                selected = selected
                    .OrderBy(record => record.CreatedAt)
                    .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
                    .Take(query.Take)
                    .ToList();
            }

            if (documents.Count < CandidatePageSize)
                break;
            skip += documents.Count;
        }

        return selected
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(query.Take)
            .ToArray();
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
    string? TenantId,
    WorkflowDispatchRecord Record)
{
    public static WorkflowDispatchDocument From(WorkflowDispatchRecord record) => new(
        ElsaRuntimeStorageManifest.WorkflowDispatchCollection,
        record.ParentWorkflowExecutionId,
        record.ChildWorkflowExecutionId,
        record.Status.ToString(),
        record.TenantId,
        record);
}
