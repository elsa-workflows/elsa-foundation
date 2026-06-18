using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="ActivityDefinitionVersion"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;ActivityDefinitionVersion&gt;</c> surface with
/// intent-revealing operations. Methods that load the owning <see cref="ActivityDefinition"/> express
/// that explicitly, so a non-relational provider can satisfy them with a second read instead of a join.
/// </summary>
public interface IActivityDefinitionVersionStore
{
    /// <summary>Gets the version with the given id, throwing if it does not exist.</summary>
    Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>Gets the version with the given id including its owning definition, throwing if it does not exist.</summary>
    Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the single version of <paramref name="definitionId"/> whose normalised SemVer sort key
    /// equals <paramref name="semVerSortKey"/>, or <c>null</c> if none. Matching on the precomputed
    /// sort key keeps the store free of any SemVer knowledge.
    /// </summary>
    Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default);

    /// <summary>Lists every version of the given definition.</summary>
    Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Lists every persisted version across all definitions.</summary>
    Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default);
}
