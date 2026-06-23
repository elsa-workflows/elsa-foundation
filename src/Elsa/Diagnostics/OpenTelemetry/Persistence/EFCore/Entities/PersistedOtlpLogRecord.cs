namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

public sealed class PersistedOtlpLogRecord
{
    public long Id { get; set; }
    public string LogRecordId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string SeverityText { get; set; } = string.Empty;
    public int? SeverityNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? AttributesJson { get; set; }
}
