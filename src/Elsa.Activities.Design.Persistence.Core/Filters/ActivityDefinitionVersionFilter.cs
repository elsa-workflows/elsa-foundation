using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionVersionFilter : IFilter<ActivityDefinitionVersion>
{
    public string? Id { get; set; }

    public string? DefinitionId { get; set; }

    public string? ImplementationKind { get; set; }

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
        if (!string.IsNullOrWhiteSpace(ImplementationKind))
            queryable = queryable.Where(x => x.ImplementationKind == ImplementationKind);
        if (!string.IsNullOrWhiteSpace(SearchTerm))
            queryable = queryable.Where(x => x.Id.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase) || x.DefinitionId!.Contains(SearchTerm, StringComparison.CurrentCultureIgnoreCase));

        return queryable;
    }
}
