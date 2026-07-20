using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionStore"/>. It is the document-store
/// counterpart of <c>EFCoreWorkflowDefinitionStore</c>: it translates each closed
/// <see cref="Query{TEntity}"/> shape to an explicitly admitted Groundwork route and executes it through one
/// access-bound store session. No definition query enumerates a document collection for client evaluation.
/// </summary>
public sealed class GroundworkWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly GroundworkNamedQueryAccess<WorkflowDefinition> _reads;

    public GroundworkWorkflowDefinitionStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _reads = new GroundworkNamedQueryAccess<WorkflowDefinition>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            GroundworkDesignJson.Options,
            boundedStore,
            sessions,
            DesignPersistenceDomain.Workflow,
            "workflow definition");
    }

    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await FindByIdAsync(id, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);

    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.LoadAsync(id, token),
            cancellationToken);

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
        WorkflowDefinitionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var route = SelectRoute(filter);
        return _reads.ExecuteAsync(
            filter.TenantAgnostic == true,
            (executor, token) => executor.QueryAsync(
                route.Identity,
                filter.ToQuery(),
                route.Order,
                token),
            cancellationToken);
    }

    private static DefinitionRoute SelectRoute(WorkflowDefinitionFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            return new(
                WorkflowsDesignStorageManifest.SearchDefinitionsQuery,
                WorkflowsDesignStorageManifest.WorkflowDefinitionSearchOrder);
        }

        if (filter.Id is not null || filter.Ids is not null)
        {
            return new(
                WorkflowsDesignStorageManifest.ListDefinitionsByIdQuery,
                WorkflowsDesignStorageManifest.WorkflowDefinitionIdOrder);
        }

        if (filter.Name is not null || filter.Names is not null)
        {
            return new(
                WorkflowsDesignStorageManifest.ListDefinitionsByNameQuery,
                WorkflowsDesignStorageManifest.WorkflowDefinitionNameOrder);
        }

        if (filter.Description is not null)
        {
            return new(
                WorkflowsDesignStorageManifest.ListDefinitionsByDescriptionQuery,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDescriptionOrder);
        }

        return new(
            WorkflowsDesignStorageManifest.ListDefinitionsByIdQuery,
            WorkflowsDesignStorageManifest.WorkflowDefinitionIdOrder);
    }

    private sealed record DefinitionRoute(
        string Identity,
        IReadOnlyList<DocumentQueryOrder> Order);
}
