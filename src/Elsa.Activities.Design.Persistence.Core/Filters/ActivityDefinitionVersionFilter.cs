using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionVersionFilter : IFilter<ActivityDefinitionVersion>
{
    public string? Id { get; set; }

    public string? DefinitionId { get; set; }

    /// <summary>
    /// Exact semantic version to match. Build-metadata-insensitive (FR-013): a filter for
    /// <c>1.0.0</c> matches a row persisted as <c>1.0.0+build</c> and vice-versa, because the match
    /// runs DB-side on <see cref="ActivityDefinitionVersion.SemVerSortKey"/> (build metadata excluded
    /// from the sort key). An unparseable value matches nothing.
    /// </summary>
    public string? Version { get; set; }

    public string? DescriptorType { get; set; }

    public ICollection<string>? Ids { get; set; }

    public string? SearchTerm { get; set; }

    public bool? TenantAgnostic { get; set; }

    public virtual IQueryable<ActivityDefinitionVersion> Apply(IQueryable<ActivityDefinitionVersion> queryable)
    {
        if (Id != null)
            queryable = queryable.Where(x => x.Id == Id);
        if (Ids != null)
            queryable = queryable.Where(x => Ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(DefinitionId))
            queryable = queryable.Where(x => x.DefinitionId == DefinitionId);
        if (!string.IsNullOrWhiteSpace(Version))
        {
            // Match on the normalised sort key so 1.0.0 and 1.0.0+build resolve as the same logical
            // version; an unparseable version can never match a (valid) persisted row.
            if (SemVer.TryParse(Version, out var semVer))
            {
                var sortKey = semVer.ToSortKey();
                queryable = queryable.Where(x => x.SemVerSortKey == sortKey);
            }
            else
            {
                queryable = queryable.Where(_ => false);
            }
        }
        if (!string.IsNullOrWhiteSpace(DescriptorType))
            queryable = queryable.Where(x => x.DescriptorType == DescriptorType);
        if (!string.IsNullOrWhiteSpace(SearchTerm))
            queryable = queryable.Where(x => x.Id.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.DefinitionId!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase));

        return queryable;
    }
}
