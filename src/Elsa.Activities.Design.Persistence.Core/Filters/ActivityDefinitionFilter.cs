using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;

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

    public string? SourceKind { get; init; }

    public string? SourceId { get; init; }

    public IQueryable<ActivityDefinition> Apply(IQueryable<ActivityDefinition> queryable)
    {
        if (Id != null) queryable = queryable.Where(x => x.Id == Id);
        if (Ids != null) queryable = queryable.Where(x => Ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(SearchTerm)) queryable = queryable.Where(x => x.DisplayName!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Category!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Description!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Id.Contains(SearchTerm));
        if (Category != null) queryable = queryable.Where(x => x.Category == Category);
        if (Description != null) queryable = queryable.Where(x => x.Description!.Contains(Description));
        if (ActivityTypeKey != null) queryable = queryable.Where(x => x.ActivityTypeKey == ActivityTypeKey);
        if (SourceKind != null) queryable = queryable.Where(x => x.SourceKind == SourceKind);
        if (SourceId != null) queryable = queryable.Where(x => x.SourceId == SourceId);

        return queryable;
    }
}
