namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Indicates that an Elsa closed query cannot be represented as a bounded Groundwork document query.
/// </summary>
public sealed class GroundworkQueryTranslationException : GroundworkQueryException
{
    public GroundworkQueryTranslationException(string message)
        : base(message)
    {
    }

    public GroundworkQueryTranslationException(string message, Exception innerException)
        : base(message, innerException: innerException)
    {
    }

    public GroundworkQueryTranslationException(
        string message,
        string? documentKind,
        string? queryIdentity,
        Type? entityType,
        Exception? innerException = null)
        : base(message, documentKind, queryIdentity, null, entityType, innerException)
    {
    }
}
