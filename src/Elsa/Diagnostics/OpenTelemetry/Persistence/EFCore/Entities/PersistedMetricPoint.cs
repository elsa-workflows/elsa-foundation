namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

public sealed class PersistedMetricPoint
{
    public long Id { get; set; }
    public string PointId { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double? Value { get; set; }
    public double? Sum { get; set; }
    public long? Count { get; set; }
    public string? AttributesJson { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
}
