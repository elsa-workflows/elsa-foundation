using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IActivityDefinitionStore"/>, the document-store
/// implementation of the definition-store contract. It binds each closed
/// <see cref="Query{TEntity}"/> shape to an explicitly admitted Groundwork route and executes it through one
/// access-bound store session, without collection enumeration or client-side filtering.
/// </summary>
public sealed class GroundworkActivityDefinitionStore : IActivityDefinitionStore
{
    private readonly GroundworkNamedQueryAccess<ActivityDefinition> _reads;

    public GroundworkActivityDefinitionStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _reads = new GroundworkNamedQueryAccess<ActivityDefinition>(
            store,
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            GroundworkActivitiesDesignJson.Options,
            boundedStore,
            sessions,
            DesignPersistenceDomain.Activity,
            "activity definition");
    }

    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await _reads.ExecuteAsync(
               acrossScopes: false,
               (executor, token) => executor.LoadAsync(id, token),
               cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);

    public Task<ActivityDefinition?> FindAsync(
        ActivityDefinitionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var route = SelectRoute(filter);
        return _reads.ExecuteAsync(
            filter.TenantAgnostic == true,
            (executor, token) => executor.FirstOrDefaultAsync(
                route.Identity,
                filter.ToQuery(),
                route.Order,
                token),
            cancellationToken);
    }

    public Task<IReadOnlyList<ActivityDefinition>> ListAsync(
        ActivityDefinitionFilter filter,
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

    public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            async (executor, token) =>
            {
                var byId = await executor.LoadAsync(id, token);
                return byId ?? await executor.FirstOrDefaultAsync(
                    ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                    Query<ActivityDefinition>.Where(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey),
                    ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder,
                    token);
            },
            cancellationToken);

    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.AnyAsync(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                Query<ActivityDefinition>.Where(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey),
                token),
            cancellationToken);

    private static DefinitionRoute SelectRoute(ActivityDefinitionFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            return new(
                ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionSearchOrder);
        }

        if (filter.Id is not null || filter.Ids is not null)
        {
            return new(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionIdOrder);
        }

        if (filter.ActivityTypeKey is not null || filter.ActivityTypeKeys is not null)
        {
            return new(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder);
        }

        if (filter.Category is not null)
        {
            return new(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder);
        }

        if (filter.DisplayName is not null)
        {
            return new(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByDisplayNameQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder);
        }

        if (filter.Description is not null)
        {
            return new(
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByDescriptionQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionOrder);
        }

        return new(
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
            ActivitiesDesignStorageManifest.ActivityDefinitionIdOrder);
    }

    private sealed record DefinitionRoute(
        string Identity,
        IReadOnlyList<DocumentQueryOrder> Order);
}
