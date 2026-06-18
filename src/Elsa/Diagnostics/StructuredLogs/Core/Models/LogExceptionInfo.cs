namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A transport-neutral snapshot of an exception attached to a log entry. The inner-exception chain is
/// captured depth-bounded so a deeply nested exception cannot produce an unbounded payload.
/// </summary>
public sealed record LogExceptionInfo(
    string Type,
    string Message,
    string? StackTrace = null,
    LogExceptionInfo? Inner = null);
