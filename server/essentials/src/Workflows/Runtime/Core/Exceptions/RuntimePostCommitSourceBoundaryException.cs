namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A handler requiring durable source identity cannot proceed; its outbox responsibility remains owed.</summary>
public sealed class RuntimePostCommitSourceBoundaryException : InvalidOperationException
{
    public RuntimePostCommitSourceBoundaryException(string? sourceOutboxItemId, string intentKind,
        Exception innerException, bool isUnavailable)
        : base("The required durable post-commit source could not be verified; delivery remains owed.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        ArgumentNullException.ThrowIfNull(innerException);
        if (sourceOutboxItemId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId);
        SourceOutboxItemId = sourceOutboxItemId;
        IntentKind = intentKind;
        IsUnavailable = isUnavailable;
    }
    public string? SourceOutboxItemId { get; }
    public string IntentKind { get; }
    public bool IsUnavailable { get; }
}
