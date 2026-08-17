using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>v2 Groundwork activity-definition store backed solely by the public row/query APIs.</summary>
public sealed class GroundworkActivityDefinitionStore(GroundworkV2ActivityDesignStore store) : IActivityDefinitionStore
{
    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            return Deserialize<ActivityDefinition>(await store.LoadAsync(
                       ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, id, cancellationToken))
                   ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);
        }
        catch (DesignPersistenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DesignPersistenceException(
                DesignPersistenceDomain.Activity,
                DesignPersistenceFailureKind.Serialization,
                "load",
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                exception);
        }
        catch (IOException exception)
        {
            throw new DesignPersistenceException(
                DesignPersistenceDomain.Activity,
                DesignPersistenceFailureKind.Provider,
                "load",
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                exception);
        }
    }

    public async Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var document = await store.FirstOrDefaultAsync(
            CreateQuery(filter), cancellationToken, filter.TenantAgnostic == true);
        return Deserialize<ActivityDefinition>(document);
    }

    public async Task<IReadOnlyList<ActivityDefinition>> ListAsync(
        ActivityDefinitionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = CreateQuery(filter);
        var documents = await ActivityDesignQueryPager.QueryAllAsync(
            store,
            query.DocumentKind,
            query.Identity,
            query.Clauses,
            query.Order,
            cancellationToken,
            filter.TenantAgnostic == true);
        return documents
            .Select(Deserialize<ActivityDefinition>)
            .Where(definition => definition is not null)
            .Cast<ActivityDefinition>()
            .ToArray();
    }

    public async Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(
        string id,
        string activityTypeKey,
        CancellationToken cancellationToken = default)
    {
        var byId = Deserialize<ActivityDefinition>(await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, id, cancellationToken));
        if (byId is not null)
            return byId;

        var document = await store.FirstOrDefaultAsync(
            CreateQuery(
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, activityTypeKey))],
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder),
            cancellationToken);
        return Deserialize<ActivityDefinition>(document);
    }

    public async Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
        => await FindByActivityTypeKeyAsync(activityTypeKey, cancellationToken) is not null;

    private async Task<ActivityDefinition?> FindByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken)
    {
        var document = await store.FirstOrDefaultAsync(
            CreateQuery(
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, activityTypeKey))],
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder),
            cancellationToken);
        return Deserialize<ActivityDefinition>(document);
    }

    private static ActivityDesignQuery CreateQuery(ActivityDefinitionFilter filter)
    {
        var clauses = new List<ActivityDesignQueryClause>();
        if (filter.Id is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionIdField, filter.Id)));
        if (filter.Ids is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.In(
                ActivitiesDesignStorageManifest.ActivityDefinitionIdField, filter.Ids.Cast<object?>())));
        if (filter.ActivityTypeKey is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, filter.ActivityTypeKey)));
        if (filter.ActivityTypeKeys is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.In(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, filter.ActivityTypeKeys.Cast<object?>())));
        if (filter.Category is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField, filter.Category)));
        if (filter.DisplayName is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField, filter.DisplayName)));
        if (filter.Description is not null)
            clauses.Add(ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Contains(
                ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField, filter.Description)));
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            clauses.Add(ActivityDesignQueryClause.AnyOf(
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField, filter.SearchTerm),
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, filter.SearchTerm),
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField, filter.SearchTerm),
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField, filter.SearchTerm),
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionIdField, filter.SearchTerm)));
        }

        var (identity, order) = SelectRoute(filter);
        return CreateQuery(clauses, identity, order);
    }

    private static ActivityDesignQuery CreateQuery(
        IReadOnlyList<ActivityDesignQueryClause> clauses,
        string identity,
        IReadOnlyList<ActivityDesignQueryOrder> order)
    {
        return new(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            identity,
            clauses,
            order);
    }

    private static (string Identity, IReadOnlyList<ActivityDesignQueryOrder> Order) SelectRoute(ActivityDefinitionFilter filter)
    {
        // Route selection mirrors the public named-query contract: the most selective supplied
        // identity chooses both the physical index admission and the deterministic tie-break order;
        // remaining filters are residual predicates on that route.
        if (filter.Id is not null)
            return (ActivitiesDesignStorageManifest.FindActivityDefinitionByIdQuery,
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ActivityDefinitionIdField)]);
        if (filter.Ids is not null)
            return (ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ActivityDefinitionIdField)]);
        if (filter.ActivityTypeKey is not null || filter.ActivityTypeKeys is not null)
            return (ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder);
        if (filter.Category is not null)
            return (ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder);
        if (filter.DisplayName is not null)
            return (ActivitiesDesignStorageManifest.ListActivityDefinitionsByDisplayNameQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder);
        if (filter.Description is not null)
            return (ActivitiesDesignStorageManifest.ListActivityDefinitionsByDescriptionQuery,
                ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionOrder);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            return (ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
                [new(ActivitiesDesignStorageManifest.ActivityDefinitionIdField)]);

        return (ActivitiesDesignStorageManifest.ListAllActivityDefinitionsQuery,
            ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder);
    }

    private static T? Deserialize<T>(ActivityDesignDocument? document) where T : Elsa.Primitives.Entities.Entity
        => document is null
            ? null
            : JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(
                document.ContentJson, GroundworkActivitiesDesignJson.Options)?.Entity;
}
