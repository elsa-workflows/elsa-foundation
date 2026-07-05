namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown when publishing a workflow definition version whose computed version string already exists
/// for the definition (issue #404) — the signature of a concurrent publish that computed the same next
/// version. On EF Core the unique index on (DefinitionId, Version) would surface this as a provider
/// exception; on Groundwork, documents are keyed only by their own id, so without this check the
/// duplicate would persist silently.
/// </summary>
public sealed class WorkflowDefinitionVersionConflictException(string definitionId, string version)
    : Exception($"A version '{version}' of workflow definition '{definitionId}' already exists; a concurrent publish likely computed the same next version.")
{
    public string DefinitionId { get; } = definitionId;
    public string Version { get; } = version;
}
