using System.Globalization;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>Durable tenant-scoped workflow test-scope lifecycle replacement.</summary>
public sealed class GroundworkWorkflowTestScopeStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null) : IWorkflowTestScopeStore, IWorkflowTestScopeAdmissionStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    internal IDocumentStore DocumentStore => store;

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Workflow test-scope queries require a bounded document store.");

    public async ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureContext(scope);
        while (true)
        {
            var existing = await LoadAsync(scope.ScopeId, cancellationToken);
            var candidate = WorkflowTestScopeTransitions.Create(existing?.Record, scope, createdAt);
            if (existing is not null && ReferenceEquals(candidate, existing.Record))
                return existing.Record;
            var result = await SaveAsync(candidate, existing?.Version ?? 0, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return candidate;
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException("Groundwork rejected workflow test-scope creation.");
        }
    }

    public async ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var record = (await LoadAsync(scopeId, cancellationToken))?.Record;
        if (record is not null)
            EnsureContext(record.Scope);
        return record;
    }

    public async ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        while (true)
        {
            var existing = await LoadAsync(request.ScopeId, cancellationToken);
            if (existing is not null)
                EnsureContext(existing.Record.Scope);
            var transition = WorkflowTestScopeTransitions.Close(existing?.Record, request);
            if (transition.Record is null || transition.Disposition != WorkflowTestScopeCloseDisposition.Accepted)
                return transition;
            var result = await SaveAsync(transition.Record, existing!.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return transition;
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException("Groundwork rejected workflow test-scope closure.");
        }
    }

    public async ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        while (true)
        {
            var existing = await LoadAsync(scopeId, cancellationToken)
                ?? throw new InvalidOperationException("The workflow test scope was not found for closure completion.");
            EnsureContext(existing.Record.Scope);
            var candidate = WorkflowTestScopeTransitions.Complete(existing.Record, completedAt);
            if (ReferenceEquals(candidate, existing.Record))
                return existing.Record;
            var result = await SaveAsync(candidate, existing.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return candidate;
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException("Groundwork rejected workflow test-scope completion.");
        }
    }

    public async ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var documents = query.State is { } state
            ? await QueryByStatePageAsync(state, query.ContinuationToken, query.PageSize + 1, cancellationToken)
            : await QueryResumablePageAsync(query, cancellationToken);
        var candidates = documents
            .Select(serializer.Deserialize<WorkflowTestScopeDocument>)
            .Select(document => document.Record)
            .Where(record => query.State is { } state
                ? record.State == state
                : record.State == WorkflowTestScopeState.Closing ||
                  record.State == WorkflowTestScopeState.Open && record.Scope.IsExpired(query.ObservedAt))
            .Where(record => query.ContinuationToken is null ||
                StringComparer.Ordinal.Compare(record.Scope.ScopeId, query.ContinuationToken) > 0)
            .OrderBy(record => record.Scope.ScopeId, StringComparer.Ordinal)
            .Take(query.PageSize + 1)
            .ToArray();
        foreach (var record in candidates)
            EnsureContext(record.Scope);
        var items = candidates.Take(query.PageSize).ToArray();
        return new WorkflowTestScopePage(
            items,
            candidates.Length > query.PageSize ? items[^1].Scope.ScopeId : null);
    }

    private async ValueTask<IReadOnlyCollection<DocumentEnvelope>> QueryByStatePageAsync(
        WorkflowTestScopeState state,
        string? afterScopeId,
        int take,
        CancellationToken cancellationToken)
    {
        var clauses = new List<DocumentQueryClause>
        {
            Equal(ElsaRuntimeStorageManifest.StateField, state.ToString())
        };
        if (afterScopeId is not null)
            clauses.Add(GreaterThan(ElsaRuntimeStorageManifest.ScopeIdField, afterScopeId));
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeStorageManifest.ListWorkflowTestScopesByStatePageQuery,
                clauses,
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.StateField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ScopeIdField)
                ],
                take: take),
            cancellationToken);
        return result.Documents;
    }

    private async ValueTask<IReadOnlyCollection<DocumentEnvelope>> QueryResumablePageAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken)
    {
        var take = query.PageSize + 1;
        var closing = await QueryByStatePageAsync(
            WorkflowTestScopeState.Closing,
            query.ContinuationToken,
            take,
            cancellationToken);
        var clauses = new List<DocumentQueryClause>
        {
            Equal(ElsaRuntimeStorageManifest.StateField, WorkflowTestScopeState.Open.ToString()),
            DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                ElsaRuntimeStorageManifest.ExpiresAtField,
                query.ObservedAt.ToString("O", CultureInfo.InvariantCulture)))
        };
        if (query.ContinuationToken is not null)
            clauses.Add(GreaterThan(ElsaRuntimeStorageManifest.ScopeIdField, query.ContinuationToken));
        var expiredOpen = await BoundedStore.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
                ElsaRuntimeStorageManifest.ListExpiredOpenWorkflowTestScopesQuery,
                clauses,
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.StateField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ScopeIdField)
                ],
                take: take),
            cancellationToken);
        return closing.Concat(expiredOpen.Documents).ToArray();
    }

    public async ValueTask AssertOpenAsync(
        WorkflowTestScope scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureContext(scope);
        var loaded = await LoadAsync(scope.ScopeId, cancellationToken);
        if (loaded is null || loaded.Record.State != WorkflowTestScopeState.Open ||
            loaded.Record.Scope.IsExpired(observedAt) ||
            !WorkflowTestScope.ContextEquals(loaded.Record.Scope, scope))
        {
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
        }

        var touch = await SaveAsync(loaded.Record, loaded.Version, cancellationToken);
        if (touch.Status != DocumentStoreWriteStatus.Saved)
            throw new TestScopeAdmissionException("The workflow test scope changed during admission.");
    }

    internal async ValueTask AssertClosingAsync(
        WorkflowTestScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureContext(scope);
        var loaded = await LoadAsync(scope.ScopeId, cancellationToken);
        if (loaded is null || loaded.Record.State != WorkflowTestScopeState.Closing ||
            !WorkflowTestScope.ContextEquals(loaded.Record.Scope, scope))
        {
            throw new InvalidOperationException("The workflow test scope is not closing in the current persistence context.");
        }

        // A same-value CAS touch places cleanup on the same durable fence as close and admission.
        // Whichever transaction commits the scope version first is authoritative for this page.
        var touch = await SaveAsync(loaded.Record, loaded.Version, cancellationToken);
        if (touch.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException("The workflow test scope changed during cleanup.");
    }

    private async ValueTask<LoadedScope?> LoadAsync(string scopeId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
            GroundworkPhysicalDocumentId.FromLogicalId(scopeId),
            cancellationToken);
        if (envelope is null)
            return null;
        var document = serializer.Deserialize<WorkflowTestScopeDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.Record.Scope.ScopeId, scopeId))
            throw new InvalidOperationException("Groundwork workflow test-scope physical identity collision detected.");
        return new LoadedScope(document.Record, envelope.Version);
    }

    private Task<DocumentStoreWriteResult> SaveAsync(
        WorkflowTestScopeRecord record,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var document = WorkflowTestScopeDocument.From(record);
        var (version, content) = serializer.Serialize(ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind, document);
        return store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(record.Scope.ScopeId),
                version,
                content,
                expectedVersion),
            cancellationToken);
    }

    private void EnsureContext(WorkflowTestScope scope)
    {
        accessContextAccessor.Current.EnsureTenantScope(scope.TenantId);
        // PersistenceAccessContext selects the tenant storage scope. WorkflowExecutionPartition is a
        // separate immutable runtime-routing fact and is validated by ContextEquals against the stored
        // scope; treating it as the persistence tenant would reject valid tenant/partition combinations.
    }

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause GreaterThan(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(path, value));

    private sealed record LoadedScope(WorkflowTestScopeRecord Record, long Version);
}

internal sealed record WorkflowTestScopeDocument(
    string Collection,
    string State,
    string ScopeId,
    DateTimeOffset ExpiresAt,
    string Partition,
    string? TenantId,
    WorkflowTestScopeRecord Record)
{
    public static WorkflowTestScopeDocument From(WorkflowTestScopeRecord record) => new(
        ElsaRuntimeStorageManifest.WorkflowTestScopeCollection,
        record.State.ToString(),
        record.Scope.ScopeId,
        record.Scope.ExpiresAt,
        record.Scope.Partition.Value,
        record.Scope.TenantId,
        record);
}
