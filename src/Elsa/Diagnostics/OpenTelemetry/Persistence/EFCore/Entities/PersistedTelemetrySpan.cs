namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

public sealed class PersistedTelemetrySpan
{
    public long Id { get; set; }
    public string SpanRecordId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int Status { get; set; }
    public string? StatusDescription { get; set; }
    public string? AttributesJson { get; set; }
    public string? EventsJson { get; set; }
    public string? LinksJson { get; set; }
}
