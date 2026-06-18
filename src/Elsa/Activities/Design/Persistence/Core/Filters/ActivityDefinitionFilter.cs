using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Queries;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionFilter : IFilter<ActivityDefinition>
{
    public bool? TenantAgnostic { get; init; }

    public string? Id { get; init; }

    public string? ActivityTypeKey { get; init; }

    public IEnumerable<string>? Ids { get; init; }

    public string? Category { get; init; }

    public string? SearchTerm { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public IQueryable<ActivityDefinition> Apply(IQueryable<ActivityDefinition> queryable)
    {
        if (Id != null) queryable = queryable.Where(x => x.Id == Id);
        if (Ids != null) queryable = queryable.Where(x => Ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            queryable = queryable.Where(x =>
                (x.DisplayName != null && x.DisplayName.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase))
                || x.ActivityTypeKey.Contains(SearchTerm)
                || (x.Category != null && x.Category.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase))
                || (x.Description != null && x.Description.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase))
                || x.Id.Contains(SearchTerm));
        }
        if (Category != null) queryable = queryable.Where(x => x.Category == Category);
        if (Description != null) queryable = queryable.Where(x => x.Description!.Contains(Description));
        if (ActivityTypeKey != null) queryable = queryable.Where(x => x.ActivityTypeKey == ActivityTypeKey);

        return queryable;
    }

    /// <summary>
    /// Projects this filter onto the closed, provider-neutral <see cref="Query{TEntity}"/> spec. This is
    /// the shape every persistence provider can translate, replacing the <see cref="Apply"/> /
    /// <see cref="IQueryable{T}"/> path for provider-agnostic stores.
    /// </summary>
    public Query<ActivityDefinition> ToQuery()
    {
        var query = Query<ActivityDefinition>.All();

        if (Id != null) query.And(x => x.Id, QueryOp.Equal, Id);
        if (Ids != null) query.And(x => x.Id, QueryOp.In, Ids);
        if (!string.IsNullOrWhiteSpace(SearchTerm))
            query.And(x => x.DisplayName, QueryOp.Contains, SearchTerm)
                .Or(x => x.ActivityTypeKey, QueryOp.Contains, SearchTerm)
                .Or(x => x.Category, QueryOp.Contains, SearchTerm)
                .Or(x => x.Description, QueryOp.Contains, SearchTerm)
                .Or(x => x.Id, QueryOp.Contains, SearchTerm);
        if (Category != null) query.And(x => x.Category, QueryOp.Equal, Category);
        if (Description != null) query.And(x => x.Description, QueryOp.Contains, Description);
        if (ActivityTypeKey != null) query.And(x => x.ActivityTypeKey, QueryOp.Equal, ActivityTypeKey);

        if (TenantAgnostic == true) query.IgnoreTenant();

        return query;
    }
}
