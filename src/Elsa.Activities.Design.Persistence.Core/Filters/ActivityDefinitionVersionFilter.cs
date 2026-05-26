using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Persistence.Core.Filters;
/// <summary>
/// A specification to use when finding workflow definitions. Only non-null fields will be included in the conditional expression.
/// </summary>
public class ActivityDefinitionVersionFilter : IFilter<ActivityDefinitionVersion>
{
    /// <summary>
    /// Filter by the ID of the workflow definition version.
    /// </summary>
    public string? Id { get; set; }

    public string? Version { get; set; }

    public string? DefinitionId { get; set; }

    /// <summary>
    /// Filter by the IDs of the workflow definitions.
    /// </summary>
    public ICollection<string>? Ids { get; set; }

    /// <summary>
    /// Filter by the name or id of the workflow definition.
    /// </summary>
    public string? SearchTerm { get; set; }

    public bool? TenantAgnostic { get; set; }

    /// <summary>
    /// Applies the filter to the specified queryable.
    /// </summary>
    /// <param name="queryable">The queryable to apply the filter to.</param>
    /// <returns>The filtered queryable.</returns>
    public virtual IQueryable<ActivityDefinitionVersion> Apply(IQueryable<ActivityDefinitionVersion> queryable)
    {
        if (Id != null)
            queryable = queryable.Where(x => x.Id == Id);
        if (Ids != null)
            queryable = queryable.Where(x => Ids.Contains(x.Id));
        if (Version != null)
            queryable = queryable.Where(x => x.TypeInfo.AssemblyVersion == Version);
        if (!string.IsNullOrWhiteSpace(DefinitionId))
            queryable = queryable.Where(x => x.DefinitionId == DefinitionId);
        if (!string.IsNullOrWhiteSpace(SearchTerm))
            queryable = queryable.Where(x => x.Id.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.DefinitionId!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase));

        return queryable;
    }
}
