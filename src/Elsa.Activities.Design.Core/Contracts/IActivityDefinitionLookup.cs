using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionLookup
{
    Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picker query. Returns rows whose reconciliation-state sibling does NOT mark them
    /// removed (catalog presence ⋂ NOT <c>RemovedAt</c>). All filter params are AND-composed;
    /// nulls/empty leave that dimension unfiltered. Per spec FR-007, SC-009 — no live-
    /// provider enumeration; the catalog store is the single source of truth.
    /// </summary>
    Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null,
        string? category = null,
        string? searchTerm = null,
        string? displayName = null,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default);

    Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default);
}
