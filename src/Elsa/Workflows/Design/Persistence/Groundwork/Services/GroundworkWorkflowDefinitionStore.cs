using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionStore"/>. It is the document-store
/// counterpart of <c>EFCoreWorkflowDefinitionStore</c>: both translate the named operations into the closed
/// <see cref="Query{TEntity}"/> spec, one executing it against a relational <c>DbContext</c>, the other
/// against a Groundwork <see cref="IDocumentStore"/> via <see cref="GroundworkReadStore{TEntity}"/>. Because
/// both speak the same closed contract, a host that selects a Groundwork provider gets the same behaviour as
/// the relational provider without any consumer change.
/// </summary>
public sealed class GroundworkWorkflowDefinitionStore : IWorkflowDefinitionStore, IWorkflowDefinitionPageStore
{
    private readonly IBoundedDocumentStore? _boundedStore;
    private readonly GroundworkReadStore<WorkflowDefinition> _reads;

    public GroundworkWorkflowDefinitionStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
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

    public bool IsAvailable => _boundedStore is not null;

    public async Task<WorkflowDefinitionPage> QueryPageAsync(
        WorkflowDefinitionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        DocumentQueryResult result;
        try
        {
            result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery,
                    BuildPageClauses(query),
                    [
                        new DocumentQueryOrder(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
                            PhysicalSortDirection.Descending),
                        new DocumentQueryOrder(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionIdField,
                            PhysicalSortDirection.Ascending)
                    ],
                    take: query.PageSize,
                    continuation: query.ContinuationToken),
                cancellationToken);
        }
        catch (InvalidDocumentQueryContinuationException exception)
        {
            throw new ArgumentException(
                "The workflow-definition continuation token is invalid or does not belong to this query.",
                nameof(query.ContinuationToken),
                exception);
        }

        var items = result.Documents.Select(ReadPageDocument).ToArray();
        return new WorkflowDefinitionPage(items, result.NextContinuation);
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore ?? throw new InvalidOperationException(
        "Workflow-definition pages require an admitted bounded document-store runtime.");

    private static WorkflowDefinition ReadPageDocument(DocumentEnvelope envelope)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinition>>(
                envelope.ContentJson,
                GroundworkDesignJson.Options)?.Entity
                ?? throw new System.Text.Json.JsonException("The workflow-definition document is empty.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new WorkflowDefinitionPageDeserializationException(envelope.Id, exception);
        }
    }

    private static IReadOnlyList<DocumentQueryClause> BuildPageClauses(WorkflowDefinitionPageQuery query)
    {
        var clauses = new List<DocumentQueryClause>();
        switch (query.State)
        {
            case WorkflowDefinitionPageState.Active:
                clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField,
                    null)));
                break;
            case WorkflowDefinitionPageState.Deleted:
                clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.NotEqual(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField,
                    null)));
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            clauses.Add(DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, query.SearchTerm),
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionField, query.SearchTerm),
                DocumentQueryComparison.Contains(WorkflowsDesignStorageManifest.WorkflowDefinitionSearchIdField, query.SearchTerm)));
        }

        return clauses;
    }
}
