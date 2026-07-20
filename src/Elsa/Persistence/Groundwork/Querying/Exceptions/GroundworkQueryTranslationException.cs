namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Indicates that an Elsa closed query cannot be represented as a bounded Groundwork document query.
/// </summary>
public sealed class GroundworkQueryTranslationException : InvalidOperationException
{
    public GroundworkQueryTranslationException(string message)
        : base(message)
    {
    }

    public GroundworkQueryTranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
