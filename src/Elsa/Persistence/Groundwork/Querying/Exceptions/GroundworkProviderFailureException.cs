namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>Wraps a provider failure that occurred while executing an admitted Groundwork query.</summary>
public sealed class GroundworkProviderFailureException : GroundworkQueryException
{
    public GroundworkProviderFailureException(
        string message,
        string? documentKind,
        string? queryIdentity,
        string? documentId,
        Type? entityType,
        Exception innerException)
        : base(message, documentKind, queryIdentity, documentId, entityType, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
