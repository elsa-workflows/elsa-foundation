namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

public sealed class PersistedTelemetryTrace
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string? RootSpanId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long DurationTicks { get; set; }
    public int Status { get; set; }
    public string? ResourceIdsJson { get; set; }
    public string? WorkflowInstanceIdsJson { get; set; }
    public int SpanCount { get; set; }
}
