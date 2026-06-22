using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;

namespace Elsa.Activities.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="ActivityDefinition"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;ActivityDefinition&gt;</c> surface with a small set of
/// intent-revealing operations, so any host-selected provider (EF Core, Groundwork, ...) can back it.
/// Writes continue to flow through the dedicated command contracts.
/// </summary>
public interface IActivityDefinitionStore
{
    /// <summary>Gets the definition with the given id, throwing if it does not exist.</summary>
    Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Finds the first definition matching the supplied filter, or <c>null</c> if none match.</summary>
    Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Finds the definition whose surrogate id equals <paramref name="id"/> or whose natural key equals <paramref name="activityTypeKey"/>, or <c>null</c>.</summary>
    Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a definition with the given activity type key already exists.</summary>
    Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default);
}
