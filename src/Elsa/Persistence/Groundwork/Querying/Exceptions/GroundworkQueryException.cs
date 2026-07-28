namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>Base class for failures classified at the Groundwork query boundary.</summary>
public abstract class GroundworkQueryException : InvalidOperationException
{
    protected GroundworkQueryException(
        string message,
        string? documentKind = null,
        string? queryIdentity = null,
        string? documentId = null,
        Type? entityType = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DocumentKind = documentKind;
        QueryIdentity = queryIdentity;
        DocumentId = documentId;
        EntityType = entityType;
    }

    /// <summary>The Groundwork document kind involved in the failure, when known.</summary>
    public string? DocumentKind { get; }

    /// <summary>The admitted bounded query identity involved in the failure, when known.</summary>
    public string? QueryIdentity { get; }

    /// <summary>The document identifier involved in the failure, when known.</summary>
    public string? DocumentId { get; }

    /// <summary>The CLR entity type the query boundary was materializing, when known.</summary>
    public Type? EntityType { get; }
}
