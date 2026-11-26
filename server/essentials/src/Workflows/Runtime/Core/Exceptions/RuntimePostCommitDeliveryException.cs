namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Classifies post-commit delivery failure without exposing provider diagnostics.</summary>
public sealed class RuntimePostCommitDeliveryException : Exception
{
    public RuntimePostCommitDeliveryException(
        PostCommitFailureKind kind,
        string code,
        string safeSummary,
        Exception? innerException = null)
        : base(safeSummary, innerException)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeSummary);

        Kind = kind;
        Code = code;
        SafeSummary = safeSummary;
    }

    public PostCommitFailureKind Kind { get; }
    public string Code { get; }
    public string SafeSummary { get; }
}

public enum PostCommitFailureKind
{
    Transient,
    Permanent
}
