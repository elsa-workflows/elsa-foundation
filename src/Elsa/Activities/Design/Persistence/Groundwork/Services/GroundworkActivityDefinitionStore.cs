using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>v2 Groundwork activity-definition store backed solely by the public row/query APIs.</summary>
public sealed class GroundworkActivityDefinitionStore(GroundworkV2ActivityDesignStore store) : IActivityDefinitionStore
{
    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => Deserialize<ActivityDefinition>(await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, id, cancellationToken))
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);

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
        var documents = await ActivityDesignQueryPager.QueryAllOffsetAsync(
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
            CreateQuery([ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, activityTypeKey))]),
            cancellationToken);
        return Deserialize<ActivityDefinition>(document);
    }

    public async Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
        => await FindByActivityTypeKeyAsync(activityTypeKey, cancellationToken) is not null;

    private async Task<ActivityDefinition?> FindByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken)
    {
        var document = await store.FirstOrDefaultAsync(
            CreateQuery([ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, activityTypeKey))]),
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

        return CreateQuery(clauses);
    }

    private static ActivityDesignQuery CreateQuery(IReadOnlyList<ActivityDesignQueryClause> clauses)
    {
        return new(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
            clauses,
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ActivityDefinitionIdField)]);
    }

    private static T? Deserialize<T>(ActivityDesignDocument? document) where T : Elsa.Primitives.Entities.Entity
        => document is null
            ? null
            : JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(
                document.ContentJson, GroundworkActivitiesDesignJson.Options)?.Entity;
}
