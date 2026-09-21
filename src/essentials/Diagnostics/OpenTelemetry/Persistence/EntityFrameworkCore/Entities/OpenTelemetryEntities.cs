namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;

public abstract class EfOpenTelemetryScopedEntity
{
    public string ScopeKey { get; set; } = null!;
}

public abstract class EfOpenTelemetrySignalEntity : EfOpenTelemetryScopedEntity
{
    public long Sequence { get; set; }
    public string Id { get; set; } = null!;
    public string IdSearchKey { get; set; } = null!;
    public string IdOrderKey { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;
}

public sealed class OpenTelemetryResourceEntity : EfOpenTelemetryScopedEntity
{
    public string Id { get; set; } = null!;
    public string IdSearchKey { get; set; } = null!;
    public string IdOrderKey { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string ServiceNameSearchKey { get; set; } = null!;
    public string ServiceNameKey { get; set; } = null!;
    public int Status { get; set; }
    public long LastSeenTicks { get; set; }
    public short LastSeenOffsetMinutes { get; set; }
    public string PayloadJson { get; set; } = null!;
}

public sealed class OpenTelemetryTraceEntity : EfOpenTelemetrySignalEntity
{
    public string TraceId { get; set; } = null!;
    public string TraceIdSearchKey { get; set; } = null!;
    public string TraceKey { get; set; } = null!;
    public string? RootSpanId { get; set; }
    public string? Name { get; set; }
    public string? NameSearchKey { get; set; }
    public int Status { get; set; }
    public long StartTimeTicks { get; set; }
    public short StartTimeOffsetMinutes { get; set; }
    public long EndTimeTicks { get; set; }
    public short EndTimeOffsetMinutes { get; set; }
    public int SpanCount { get; set; }
}

public sealed class OpenTelemetrySpanEntity : EfOpenTelemetrySignalEntity
{
    public string TraceId { get; set; } = null!;
    public string TraceIdSearchKey { get; set; } = null!;
    public string TraceKey { get; set; } = null!;
    public string SpanId { get; set; } = null!;
    public string SpanIdSearchKey { get; set; } = null!;
    public string SpanIdOrderKey { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string ResourceIdSearchKey { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameSearchKey { get; set; } = null!;
    public int Status { get; set; }
    public long StartTimeTicks { get; set; }
    public short StartTimeOffsetMinutes { get; set; }
    public long EndTimeTicks { get; set; }
    public short EndTimeOffsetMinutes { get; set; }
}

public sealed class OpenTelemetryMetricInstrumentEntity : EfOpenTelemetryScopedEntity
{
    public string Id { get; set; } = null!;
    public string IdSearchKey { get; set; } = null!;
    public string IdOrderKey { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string ResourceIdSearchKey { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameSearchKey { get; set; } = null!;
    public long LastSeenTicks { get; set; }
    public short LastSeenOffsetMinutes { get; set; }
    public string PayloadJson { get; set; } = null!;
}

public sealed class OpenTelemetryMetricPointEntity : EfOpenTelemetrySignalEntity
{
    public string InstrumentId { get; set; } = null!;
    public string InstrumentIdSearchKey { get; set; } = null!;
    public string InstrumentName { get; set; } = null!;
    public string InstrumentNameSearchKey { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string ResourceIdSearchKey { get; set; } = null!;
    public string? ServiceName { get; set; }
    public string? ServiceNameKey { get; set; }
    public long TimestampTicks { get; set; }
    public short TimestampOffsetMinutes { get; set; }
}

public sealed class OpenTelemetryLogEntity : EfOpenTelemetrySignalEntity
{
    public string ResourceId { get; set; } = null!;
    public string ResourceIdSearchKey { get; set; } = null!;
    public string? ServiceName { get; set; }
    public string? ServiceNameKey { get; set; }
    public string? TraceId { get; set; }
    public string? TraceIdSearchKey { get; set; }
    public string? SpanId { get; set; }
    public string? SpanIdSearchKey { get; set; }
    public string SeverityText { get; set; } = null!;
    public string SeveritySearchKey { get; set; } = null!;
    public int? SeverityNumber { get; set; }
    public string Body { get; set; } = null!;
    public string BodySearchKey { get; set; } = null!;
    public long TimestampTicks { get; set; }
    public short TimestampOffsetMinutes { get; set; }
}

public sealed class OpenTelemetryCaptureLedgerEntity : EfOpenTelemetryScopedEntity
{
    public Guid BatchId { get; set; }
    public string Fingerprint { get; set; } = null!;
    public long IssuedAtTicks { get; set; }
    public short IssuedAtOffsetMinutes { get; set; }
    public int Status { get; set; }
}

public sealed class OpenTelemetryTraceSummaryEntity : EfOpenTelemetryScopedEntity
{
    public string TraceKey { get; set; } = null!;
    public string TraceId { get; set; } = null!;
    public string TraceIdSearchKey { get; set; } = null!;
    public string? RootSpanId { get; set; }
    public string? Name { get; set; }
    public string? NameSearchKey { get; set; }
    public int Status { get; set; }
    public long StartTimeTicks { get; set; }
    public short StartTimeOffsetMinutes { get; set; }
    public long EndTimeTicks { get; set; }
    public short EndTimeOffsetMinutes { get; set; }
    public int SpanCount { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string ServiceMembershipJson { get; set; } = null!;
    public string WorkflowMembershipJson { get; set; } = null!;
    public Guid Version { get; set; }
}

public enum OpenTelemetryTraceSummaryMembershipKind
{
    Resource,
    Service,
    WorkflowInstance
}

public sealed class OpenTelemetryTraceSummaryMembershipEntity : EfOpenTelemetryScopedEntity
{
    public string TraceKey { get; set; } = null!;
    public OpenTelemetryTraceSummaryMembershipKind Kind { get; set; }
    public string Value { get; set; } = null!;
    public string ValueSearchKey { get; set; } = null!;
    public string ValueKey { get; set; } = null!;
}
