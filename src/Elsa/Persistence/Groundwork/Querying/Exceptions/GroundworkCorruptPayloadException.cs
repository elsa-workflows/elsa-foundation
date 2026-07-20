namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>Indicates that a provider-returned document cannot be materialized as the requested entity.</summary>
public sealed class GroundworkCorruptPayloadException : GroundworkQueryException
{
    public GroundworkCorruptPayloadException(
        string message,
        string documentKind,
        string documentId,
        Type entityType,
        Exception? innerException = null)
        : base(message, documentKind, null, documentId, entityType, innerException)
    {
    }
}
