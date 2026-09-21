namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A logical origin of log entries. v1 exposes the single local host; the shape is retained so future
/// remote sources fit without a contract change.
/// </summary>
public sealed record LogSource
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? ServiceName { get; init; }
    public string? MachineName { get; init; }
    public int? ProcessId { get; init; }
}
