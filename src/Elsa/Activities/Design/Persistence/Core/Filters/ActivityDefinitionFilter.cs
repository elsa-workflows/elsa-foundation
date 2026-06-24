using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core.Queries;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionFilter
{
    public bool? TenantAgnostic { get; init; }

    public string? Id { get; init; }

    public string? ActivityTypeKey { get; init; }

    public IEnumerable<string>? Ids { get; init; }

    public string? Category { get; init; }

    public string? SearchTerm { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Projects this filter onto the closed, provider-neutral <see cref="Query{TEntity}"/> spec. This is
    /// the shape every persistence provider can translate.
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
        if (DisplayName != null) query.And(x => x.DisplayName, QueryOp.Equal, DisplayName);
        if (Description != null) query.And(x => x.Description, QueryOp.Contains, Description);
        if (ActivityTypeKey != null) query.And(x => x.ActivityTypeKey, QueryOp.Equal, ActivityTypeKey);

        if (TenantAgnostic == true) query.IgnoreTenant();

        return query;
    }
}
