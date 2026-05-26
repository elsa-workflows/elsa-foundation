using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionFilter : IFilter<ActivityDefinition>
{
    public bool? TenantAgnostic { get; init; }

    public string? Id { get; init; }

    public string? UniqueName { get; init; }

    public IEnumerable<string>? Ids { get; init; }

    /// <summary>
    /// The category of the activity type.
    /// </summary>
    public string? Category { get; init; }

    public string? SearchTerm { get; init; }

    /// <summary>
    /// The display name of the activity type.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The description of the activity type.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this activity type is selectable from activity pickers.
    /// </summary>
    public bool? IsBrowsable { get; init; }

    public IQueryable<ActivityDefinition> Apply(IQueryable<ActivityDefinition> queryable)
    {
        if (Id != null) queryable = queryable.Where(x => x.Id == Id);
        if (Ids != null) queryable = queryable.Where(x => Ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(SearchTerm)) queryable = queryable.Where(x => x.DisplayName!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Category!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Description!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.Id.Contains(SearchTerm));
        if (IsBrowsable != null) queryable = queryable.Where(x => x.IsBrowsable == IsBrowsable);
        if (Category != null) queryable = queryable.Where(x => x.Category == Category);
        if (Description != null) queryable = queryable.Where(x => x.Description!.Contains(Description));
        if (UniqueName != null)
            queryable = queryable.Where(x => x.UniqueName == UniqueName);

        return queryable;
    }
}
