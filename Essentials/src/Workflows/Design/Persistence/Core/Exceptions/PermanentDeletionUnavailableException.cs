namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown when permanent deletion is requested on a host that composes no publication check, so it cannot tell
/// whether the definition is still published. Permanently deleting in that state can strand a live publication
/// held by another node against the same design catalog, which is unrecoverable, so the operation is refused
/// outright rather than attempted.
/// </summary>
public sealed class PermanentDeletionUnavailableException(string definitionId)
    : InvalidOperationException(
        $"Permanent deletion of workflow definition '{definitionId}' is unavailable on this host: it does not " +
        "compose the publishing vertical, so it cannot verify whether the definition is still published, and " +
        "deleting it could strand a live publication held elsewhere against the same design catalog. Soft-delete " +
        $"it here instead (DELETE design/workflows/definitions/{definitionId}), and permanently delete it from a " +
        "host that composes publishing.")
{
    public string DefinitionId { get; } = definitionId;
}
