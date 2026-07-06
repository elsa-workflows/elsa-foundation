namespace Elsa.Activities.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown when adding an activity definition version whose author-supplied version string already
/// exists for the definition. The reconciler guards this on its path (an existing
/// <c>(DefinitionId, sortKey)</c> is a hash-mismatch conflict or a tolerated duplicate); the
/// author-supplied API path carries no hash comparison to make, so any existing
/// <c>(DefinitionId, sortKey)</c> is a collision regardless of content. On EF Core a unique index on
/// (DefinitionId, Version) would surface this as a provider exception; on Groundwork, documents are
/// keyed only by their own id, so without this check the duplicate would persist silently.
/// </summary>
public sealed class ActivityDefinitionVersionConflictException(string definitionId, string version)
    : Exception($"A version '{version}' of activity definition '{definitionId}' already exists; adding it again would silently collide.")
{
    public string DefinitionId { get; } = definitionId;
    public string Version { get; } = version;
}
