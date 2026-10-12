namespace Elsa.Workflows.Design.Reconciliation.Json.Exceptions;

/// <summary>
/// Raised (§2.23.5) when the configured JSON catalog file is missing or cannot be parsed into an
/// array of workflow reconciliation models. Carries the offending file path so the fault is
/// actionable; no raw <c>IOException</c>/<c>JsonException</c> escapes the reader.
/// </summary>
public sealed class InvalidWorkflowCatalogJsonException(string filePath, string reason, Exception? innerException = null)
    : Exception($"Could not read workflow catalog JSON at '{filePath}': {reason}", innerException)
{
    public string FilePath { get; } = filePath;
}
