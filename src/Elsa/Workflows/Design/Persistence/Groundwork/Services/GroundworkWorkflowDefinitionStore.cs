using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
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
public sealed class GroundworkWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly GroundworkReadStore<WorkflowDefinition> _reads;

    public GroundworkWorkflowDefinitionStore(IDocumentStore store)
    {
        _reads = new GroundworkReadStore<WorkflowDefinition>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.ByCollectionIndex,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            GroundworkDesignJson.Options);
    }

    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await FindByIdAsync(id, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);

    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(Query<WorkflowDefinition>.Where(x => x.Id, QueryOp.Equal, id), cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(filter.ToQuery(), cancellationToken);
}
