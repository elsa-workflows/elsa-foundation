namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>Indicates that an admitted Groundwork query cannot run in the current runtime context.</summary>
public sealed class GroundworkQueryReadinessException : GroundworkQueryException
{
    public GroundworkQueryReadinessException(
        string message,
        string? documentKind,
        string? queryIdentity,
        string? documentId,
        Type? entityType,
        Exception? innerException = null)
        : base(message, documentKind, queryIdentity, documentId, entityType, innerException)
    {
    }
}
