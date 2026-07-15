using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IActivityDefinitionStore"/>, the document-store
/// counterpart of <c>EFCoreActivityDefinitionStore</c>. Both translate the named operations into the closed
/// <see cref="Query{TEntity}"/> spec; this one executes it against a Groundwork <see cref="IDocumentStore"/>
/// via <see cref="GroundworkReadStore{TEntity}"/>, so a host that selects a Groundwork provider gets the same
/// behaviour as the relational provider without any consumer change.
/// </summary>
public sealed class GroundworkActivityDefinitionStore : IActivityDefinitionStore
{
    private readonly GroundworkReadStore<ActivityDefinition> _reads;

    public GroundworkActivityDefinitionStore(IDocumentStore store, IBoundedDocumentStore? boundedStore = null)
    {
        _reads = new GroundworkReadStore<ActivityDefinition>(
            store,
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            GroundworkActivitiesDesignJson.Options,
            boundedStore);
    }

    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await _reads.FirstOrDefaultAsync(Query<ActivityDefinition>.Where(x => x.Id, QueryOp.Equal, id), cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);

    public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(filter.ToQuery(), cancellationToken);

    public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        => _reads.QueryAsync(filter.ToQuery(), cancellationToken);

    public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(
            Query<ActivityDefinition>.Where(x => x.Id, QueryOp.Equal, id)
                .Or(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey),
            cancellationToken);

    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
        => _reads.AnyAsync(Query<ActivityDefinition>.Where(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey), cancellationToken);
}
