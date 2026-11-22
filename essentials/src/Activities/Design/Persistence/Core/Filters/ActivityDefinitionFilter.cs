using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Filters;

public class ActivityDefinitionFilter
{
    public bool? TenantAgnostic { get; init; }

    public string? Id { get; init; }

    public string? ActivityTypeKey { get; init; }

    public IEnumerable<string>? Ids { get; init; }

    /// <summary>Batch counterpart of <see cref="ActivityTypeKey"/>: matches definitions whose natural key is in this set (<c>IN</c>).</summary>
    public IEnumerable<string>? ActivityTypeKeys { get; init; }

    public string? Category { get; init; }

    public string? SearchTerm { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

}
