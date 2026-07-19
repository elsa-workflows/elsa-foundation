using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionStore"/>. It is the document-store
/// counterpart of <c>EFCoreWorkflowDefinitionStore</c>: both translate the named operations into the closed
/// <see cref="Query{TEntity}"/> spec, one executing it against a relational <c>DbContext</c>, the other
/// against a Groundwork <see cref="IDocumentStore"/> via <see cref="GroundworkReadStore{TEntity}"/>. Because
/// both speak the same closed contract, a host that selects a Groundwork provider gets the same behaviour as
/// the relational provider without any consumer change.
/// </summary>
public sealed class GroundworkWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly IBoundedDocumentStore? _boundedStore;
    private readonly IGroundworkStoreSessionFactory? _sessions;
    private readonly GroundworkReadStore<WorkflowDefinition> _reads;

    public GroundworkWorkflowDefinitionStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
        _sessions = sessions;
        _reads = new GroundworkReadStore<WorkflowDefinition>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.ListAllQuery,
            WorkflowsDesignStorageManifest.CollectionField,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            GroundworkDesignJson.Options,
            boundedStore,
            sessions);
    }

    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await FindByIdAsync(id, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);

    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(Query<WorkflowDefinition>.Where(x => x.Id, QueryOp.Equal, id), cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(filter.ToQuery(), cancellationToken);

    public async Task<WorkflowDefinitionPage> ListPageAsync(
        WorkflowDefinitionListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        if (query.Filter.TenantAgnostic != true)
            return await ListPageAsync(BoundedStore(), query, cancellationToken);

        var sessions = _sessions ?? throw new InvalidOperationException(
            $"Tenant-agnostic queries for '{WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind}' require an authorized Groundwork session factory.");
        return await sessions.ExecutePrivilegedAcrossScopesAsync(
            async (session, token) =>
            {
                if (session.Access.Kind != global::Groundwork.Documents.Scoping.DocumentStoreAccessKind.PrivilegedAcrossScopes)
                    throw new InvalidOperationException("Tenant-agnostic persistence queries require explicit privileged across-scope access.");

                return await ListPageAsync(session.BoundedDocumentStore, query, token);
            },
            cancellationToken);
    }

    private static async Task<WorkflowDefinitionPage> ListPageAsync(
        IBoundedDocumentStore boundedStore,
        WorkflowDefinitionListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await boundedStore.QueryAsync(
            new DocumentQuery(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.PageQueryIdentity(query),
                Clauses(query),
                Orders(query),
                skip: query.Skip,
                take: query.PageSize),
            cancellationToken);

        var definitions = result.Documents
            .Select(envelope => JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinition>>(
                envelope.ContentJson,
                GroundworkDesignJson.Options)?.Entity ?? throw new InvalidOperationException(
                    $"Document '{envelope.Id}' of kind '{WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind}' could not be deserialized as {nameof(WorkflowDefinition)}."))
            .ToArray();
        return new WorkflowDefinitionPage(definitions, checked((int)result.TotalCount));
    }

    private static IReadOnlyList<DocumentQueryClause> Clauses(WorkflowDefinitionListQuery query)
    {
        var clauses = new List<DocumentQueryClause>();
        var filter = query.Filter;

        if (filter.Id is not null)
            clauses.Add(Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, filter.Id));
        if (filter.Ids is { Count: > 0 })
            clauses.Add(In(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, filter.Ids));
        if (filter.Name is not null)
            clauses.Add(Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, filter.Name));
        if (filter.Names is { Count: > 0 })
            clauses.Add(In(WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, filter.Names));
        if (filter.Description is not null)
            clauses.Add(Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionField, filter.Description));
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            clauses.Add(DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, filter.SearchTerm),
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionField, filter.SearchTerm),
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, filter.SearchTerm)));

        switch (query.Scope)
        {
            case WorkflowDefinitionLifecycleScope.Active:
                clauses.Add(Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField, null));
                break;
            case WorkflowDefinitionLifecycleScope.Deleted:
                clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.NotEqual(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField,
                    null)));
                break;
        }

        return clauses;
    }

    private static IReadOnlyList<DocumentQueryOrder> Orders(WorkflowDefinitionListQuery query)
    {
        var direction = query.SortDirection == WorkflowDefinitionSortDirection.Desc
            ? PhysicalSortDirection.Descending
            : PhysicalSortDirection.Ascending;
        var sortField = query.SortBy switch
        {
            WorkflowDefinitionSortBy.LastModifiedAt => WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
            WorkflowDefinitionSortBy.CreatedAt => WorkflowsDesignStorageManifest.WorkflowDefinitionCreatedAtField,
            _ => WorkflowsDesignStorageManifest.WorkflowDefinitionNameField
        };
        return
        [
            new DocumentQueryOrder(sortField, direction),
            new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, PhysicalSortDirection.Ascending)
        ];
    }

    private static DocumentQueryClause Equal(string path, string? value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause In(string path, IEnumerable<string> values) =>
        DocumentQueryClause.Of(DocumentQueryComparison.In(path, values));

    private IBoundedDocumentStore BoundedStore() => _boundedStore ?? throw new InvalidOperationException(
        $"Queries for '{WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind}' require an admitted bounded document-store runtime.");
}
