namespace Elsa.Persistence.Groundwork.Exceptions;

/// <summary>
/// Thrown when a persisted runtime document cannot be deserialized because its schema-version stamp
/// is unrecognized, newer than this build supports, below an explicit clean-break floor, or older
/// with no upcaster chain to bring it to the current version. Failing loudly here prevents silently hydrating suspended workflow state
/// with default values from a mismatched shape.
/// </summary>
public sealed class GroundworkRuntimeDocumentVersionException : Exception
{
    public GroundworkRuntimeDocumentVersionException(string message)
        : base(message)
    {
    }

    public GroundworkRuntimeDocumentVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
