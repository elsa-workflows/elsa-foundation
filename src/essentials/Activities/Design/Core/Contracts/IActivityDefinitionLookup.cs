using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionLookup
{
    Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picker query. Returns rows whose reconciliation-state sibling does NOT mark them
    /// removed (catalog presence ⋂ NOT <c>RemovedAt</c>). All filter params are AND-composed;
    /// nulls/empty leave that dimension unfiltered. <paramref name="tenantAgnostic"/> set to
    /// <c>true</c> ignores the ambient tenant scope. Per spec FR-007, SC-009 — no live-
    /// provider enumeration; the catalog store is the single source of truth.
    /// </summary>
    Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null,
        string? category = null,
        string? searchTerm = null,
        string? displayName = null,
        string? description = null,
        bool? tenantAgnostic = null,
        CancellationToken cancellationToken = default);

    Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nullable counterpart to <see cref="GetVersion"/> (repo Get-throws / Find-nullable convention):
    /// returns the version, or <c>null</c> when no version with <paramref name="versionId"/> exists.
    /// Prefer this on paths that treat a missing version as a normal outcome (e.g. draft validation)
    /// rather than catching <c>EntityNotFoundException</c> from <see cref="GetVersion"/> at each call site.
    /// </summary>
    Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default);

    Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default);
}
