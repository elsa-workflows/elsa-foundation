using Groundwork.Kernel;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Ordinary Groundwork v2 declarations for the OpenTelemetry adapter.  The declarations are
/// intentionally independent from the legacy diagnostic-record and document manifests.
/// </summary>
public static class V2OpenTelemetryStorageSchema
{
    public const string TraceUnitId = "elsa-otel-traces-v2";
    public const string SpanUnitId = "elsa-otel-spans-v2";
    public const string MetricPointUnitId = "elsa-otel-metric-points-v2";
    public const string LogUnitId = "elsa-otel-logs-v2";
    public const string ResourceUnitId = "elsa-otel-resources-v2";
    public const string InstrumentUnitId = "elsa-otel-instruments-v2";
    public const string CaptureLedgerUnitId = "elsa-otel-capture-ledger-v3";
    public const string TraceSummaryUnitId = "elsa-otel-trace-summaries-v3";

    public const string TraceProfile = "trace-summary-v2";
    public const string Sequence = "sequence";
    public const string Id = "id";
    public const string TraceId = "traceId";
    public const string TraceKey = "traceKey";
    public const string TraceIdSearchKey = "traceIdSearchKey";
    public const string NameSearchKey = "nameSearchKey";
    public const string SpanId = "spanId";
    public const string ResourceId = "resourceId";
    public const string ResourceIds = "resourceIds";
    public const string ResourceKeys = "resourceKeys";
    public const string ServiceNames = "serviceNames";
    public const string ServiceName = "serviceName";
    public const string WorkflowInstanceId = "workflowInstanceId";
    public const string WorkflowInstanceIds = "workflowInstanceIds";
    public const string RootSpanId = "rootSpanId";
    public const string Name = "traceName";
    public const string Status = "status";
    public const string StartTime = "startTime";
    public const string EndTime = "endTime";
    public const string SpanCount = "spanCount";
    public const string InstrumentId = "instrumentId";
    public const string InstrumentName = "instrumentName";
    public const string Timestamp = "timestamp";
    public const string SeverityText = "severityText";
    public const string SeverityNumber = "severityNumber";
    public const string Body = "body";
    public const string Payload = "payload";
    public const string LastSeen = "lastSeen";
    public const string Kind = "kind";
    public const string Fingerprint = "fingerprint";
    public const string BatchId = "batchId";
    public const string CreatedAt = "createdAt";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
    [
        CreateTraces(),
        CreateSpans(),
        CreateMetricPoints(),
        CreateLogs(),
        CreateResources(),
        CreateInstruments(),
        CreateCaptureLedger(),
        CreateTraceSummaries()
    ];

    public static StorageUnit CreateTraces() =>
        StorageUnit.Declare(TraceUnitId, "elsa_otel_traces_v2")
            .Int64(Sequence, c => c.Required().ProviderSequence())
            .String(Id, 256, c => c.Required())
            .String(TraceId, 256, c => c.Required())
            .String(TraceKey, 64, c => c.Required())
            .String(RootSpanId, 256)
            .String(ResourceId, 512, c => c.Required())
            .String(ServiceName, 512)
            .String(WorkflowInstanceId, 512)
            .String(Name)
            .Int64(Status, c => c.Required())
            .Timestamp(StartTime, c => c.Required())
            .Timestamp(EndTime, c => c.Required())
            .Int64(SpanCount, c => c.Required())
            .Json(Payload, c => c.Required())
            .Key(Sequence)
            .Index("elsa_otel_traces_trace", TraceId)
            .Index("elsa_otel_traces_trace_key", TraceKey)
            .Index("elsa_otel_traces_start", StartTime)
            .Index("elsa_otel_traces_service", ServiceName)
            .Scoped()
            .AppendIdempotency(TimeSpan.FromHours(1), "elsa_otel_traces_append")
            .Aggregate(TraceProfile, aggregate => aggregate
                .GroupBy(TraceId)
                .FirstBy(RootSpanId, RootSpanId, StartTime)
                .FirstBy(Name, Name, StartTime)
                .Min(StartTime, StartTime)
                .Max(EndTime, EndTime)
                .Max(Status, Status)
                .SetUnion(ResourceId, ResourceId, 5_000)
                .SetUnion(ServiceName, ServiceName, 5_000)
                .SetUnion(WorkflowInstanceId, WorkflowInstanceId, 5_000)
                .Sum(SpanCount, SpanCount))
            .Retention(0, Sequence)
            .RetentionIdempotency(TimeSpan.FromHours(1), "elsa_otel_traces_retention")
            .Build();

    public static StorageUnit CreateTraceSummaries() =>
        StorageUnit.Declare(TraceSummaryUnitId, "elsa_otel_trace_summaries_v3")
            .String(TraceKey, 64, c => c.Required())
            .String(TraceId, 256, c => c.Required())
            .String(TraceIdSearchKey, 1536, c => c.Required())
            .String(RootSpanId, 256)
            .String(Name, V2OpenTelemetryCodec.MaximumSummaryNameCodeUnits)
            .String(NameSearchKey, V2OpenTelemetryCodec.MaximumSummaryNameSearchKeyCodeUnits)
            .Int64(Status, c => c.Required())
            .Timestamp(StartTime, c => c.Required())
            .Timestamp(EndTime, c => c.Required())
            .Int64(SpanCount, c => c.Required())
            .Json(ResourceIds, c => c.Required())
            .Json(ResourceKeys, c => c.Required())
            .Json(ServiceNames, c => c.Required())
            .Json(WorkflowInstanceIds, c => c.Required()
                .ElementSearchKey(PortableCollation.UnicodeOrdinalIgnoreCase, 512))
            .Json(Payload, c => c.Required())
            .Key(TraceKey)
            .Index("elsa_otel_trace_summaries_start", index =>
                index.Descending(StartTime).Ascending(TraceKey))
            .OptimisticConcurrency()
            .Scoped()
            .Build();

    public static StorageUnit CreateSpans() => CreateSignal(
        SpanUnitId,
        "elsa_otel_spans_v2",
        (builder) => builder
            .String(TraceId, 256, c => c.Required())
            .String(TraceKey, 64, c => c.Required())
            .String(SpanId, 256, c => c.Required())
            .String(ResourceId, 512, c => c.Required())
            .String(Name, c => c.Required())
            .Int64(Status, c => c.Required())
            .Timestamp(StartTime, c => c.Required())
            .Timestamp(EndTime, c => c.Required()));

    public static StorageUnit CreateMetricPoints() => CreateSignal(
        MetricPointUnitId,
        "elsa_otel_metric_points_v2",
        (builder) => builder
            .String(InstrumentId, 256, c => c.Required())
            .String(InstrumentName, c => c.Required())
            .String(ResourceId, 512, c => c.Required())
            .String(ServiceName, 512)
            .Timestamp(Timestamp, c => c.Required()));

    public static StorageUnit CreateLogs() => CreateSignal(
        LogUnitId,
        "elsa_otel_logs_v2",
        (builder) => builder
            .String(ResourceId, 512, c => c.Required())
            .String(ServiceName, 512)
            .String(TraceId, 256)
            .String(TraceKey, 64)
            .String(SpanId, 256)
            .String(SeverityText, c => c.Required())
            .Int64(SeverityNumber)
            .String(Body, c => c.Required())
            .Timestamp(Timestamp, c => c.Required()));

    public static StorageUnit CreateResources() =>
        StorageUnit.Declare(ResourceUnitId, "elsa_otel_resources_v2")
            .String(Id, 512, c => c.Required())
            .String(ServiceName, 512, c => c.Required())
            .Int64(Status, c => c.Required())
            .Timestamp(LastSeen, c => c.Required())
            .Json(Payload, c => c.Required())
            .Key(Id)
            .Index("elsa_otel_resources_last_seen", LastSeen)
            .Index("elsa_otel_resources_service", ServiceName)
            .Index("elsa_otel_resources_status", Status)
            .Scoped()
            .Retention(0, LastSeen)
            .Build();

    public static StorageUnit CreateInstruments() =>
        StorageUnit.Declare(InstrumentUnitId, "elsa_otel_instruments_v2")
            .String(Id, 512, c => c.Required())
            .String(ResourceId, 512, c => c.Required())
            .String(InstrumentName, c => c.Required())
            .Int64(Kind, c => c.Required())
            .Timestamp(LastSeen, c => c.Required())
            .Json(Payload, c => c.Required())
            .Key(Id)
            .Index("elsa_otel_instruments_resource", ResourceId)
            .Index("elsa_otel_instruments_last_seen", LastSeen)
            .Scoped()
            .Retention(0, LastSeen)
            .Build();

    public static StorageUnit CreateCaptureLedger() =>
        StorageUnit.Declare(CaptureLedgerUnitId, "elsa_otel_capture_ledger_v3")
            .String(BatchId, 64, c => c.Required())
            .String(Fingerprint, 128, c => c.Required())
            .Timestamp(CreatedAt, c => c.Required())
            .String(Status, 32, c => c.Required())
            .Key(BatchId)
            .Index("elsa_otel_capture_created", CreatedAt)
            .Scoped()
            .Retention(0, CreatedAt)
            .Build();

    private static StorageUnit CreateSignal(
        string id,
        string name,
        Func<StorageDeclarationBuilder, StorageDeclarationBuilder> fields) =>
        fields(StorageUnit.Declare(id, name)
                .Int64(Sequence, c => c.Required().ProviderSequence())
                .String(Id, 512, c => c.Required())
                .Json(Payload, c => c.Required()))
            .Key(Sequence)
            .Scoped()
            .AppendIdempotency(TimeSpan.FromHours(1), $"{id.Replace('-', '_')}_append")
            .Retention(0, Sequence)
            .Build();
}
