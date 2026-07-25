namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;

/// <summary>Canonical field names projected from immutable OpenTelemetry records.</summary>
public static class RecordFields
{
    public const string TraceId = "traceId";
    public const string RootSpanId = "rootSpanId";
    public const string SpanId = "spanId";
    public const string ParentSpanId = "parentSpanId";
    public const string ResourceId = "resourceId";
    public const string ServiceName = "serviceName";
    public const string WorkflowInstanceId = "workflowInstanceId";
    public const string Status = "status";
    public const string StartTime = "startTime";
    public const string EndTime = "endTime";
    public const string Name = "name";
    public const string SpanCount = "spanCount";
    public const string InstrumentId = "instrumentId";
    public const string InstrumentName = "instrumentName";
    public const string Timestamp = "timestamp";
    public const string SeverityText = "severityText";
    public const string SeverityNumber = "severityNumber";
    public const string Body = "body";
    public const string OrderKey = "orderKey";
}
