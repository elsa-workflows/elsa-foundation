namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Structured capture of a runtime fault. Replaces the two divergent ad-hoc conventions that previously
/// existed (the drainer's <c>exception.ToString()</c> and the outbox's message-or-type-name string): a single
/// shape carrying the exception type, message, and — only when diagnostics are enabled — the stack trace.
/// </summary>
public sealed record RuntimeFaultInfo(
    string ExceptionType,
    string Message,
    string? StackTrace = null)
{
    /// <summary>A compact, allocation-light single-line summary suitable for string fault fields and logs.</summary>
    public string ToSummaryString() => $"{ExceptionType}: {Message}";
}

/// <summary>
/// Options controlling <c>DefaultRuntimeFaultCapturePolicy</c> (engine package). The
/// stack trace is captured only when <see cref="CaptureStackTrace"/> is enabled so the hot path does not
/// unconditionally materialize a full stack string (which may be large or leak sensitive data).
/// </summary>
public sealed class RuntimeFaultCaptureOptions
{
    public bool CaptureStackTrace { get; set; }
}
