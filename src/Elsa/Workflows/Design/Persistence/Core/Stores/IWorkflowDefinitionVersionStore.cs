using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="WorkflowDefinitionVersion"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;WorkflowDefinitionVersion&gt;</c> surface with
/// intent-revealing operations. Methods that load the owning <see cref="WorkflowDefinition"/> express
/// that explicitly, so a non-relational provider can satisfy them with a second read instead of a join.
/// </summary>
public interface IWorkflowDefinitionVersionStore
{
    /// <summary>Gets the version with the given id, throwing if it does not exist.</summary>
    Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>Finds the version with the given id, or <c>null</c> if it does not exist.</summary>
    Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>Gets the version with the given id including its owning definition, throwing if it does not exist.</summary>
    Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>Finds the most recent version (by SemVer precedence) of the given definition, or <c>null</c>.</summary>
    Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Lists every version of the given definition.</summary>
    Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a version with the given SemVer sort key already exists for the definition.</summary>
    Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default);
}
