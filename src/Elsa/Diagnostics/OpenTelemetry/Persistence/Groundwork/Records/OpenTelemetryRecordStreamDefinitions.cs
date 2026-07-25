using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;

/// <summary>Creates the bounded Groundwork stream contracts for immutable OpenTelemetry signals.</summary>
public static class OpenTelemetryRecordStreamDefinitions
{
    public const string TraceSummaryProfileName = "trace-summary-v1";
    public const int MaxTraceRecordCapacity = 5_000;
    public const int MaxRecordsPerSignalBatch = 1_000;
    public const int RetentionRecordInterval = 500;

    private const int TraceDefinitionSchemaVersion = 2;
    private const int MaxIdentifierBytes = 512;
    private const int MaxNameBytes = 4_096;
    private const int MaxBodyBytes = 65_536;
    private const int MaxMultiValues = 256;
    private const int MaxTraceUnionValues = MaxTraceRecordCapacity * MaxMultiValues;

    /// <summary>Creates the trace-summary stream definition for a host-selected stream identity.</summary>
    public static DiagnosticRecordStreamDefinition CreateTraces(string streamId) => Definition(
        streamId,
        "elsa_open_telemetry_traces_v2",
        [
            String(RecordFields.TraceId, required: true, latestPerKey: true),
            String(RecordFields.RootSpanId),
            String(RecordFields.ResourceId, required: true, multiple: true),
            String(RecordFields.ServiceName, multiple: true),
            String(RecordFields.WorkflowInstanceId, multiple: true),
            Int64(RecordFields.Status, required: true),
            Timestamp(RecordFields.StartTime, required: true, orderable: true),
            Timestamp(RecordFields.EndTime, required: true),
            String(RecordFields.Name, maxStringBytes: MaxNameBytes),
            Int64(RecordFields.SpanCount, required: true)
        ],
        // The public trace filter has nine value slots when every optional field is supplied:
        // trace, resource, service, workflow, status, two range bounds, and two search branches.
        maxPredicateValues: 9,
        groupReductionProfiles: [CreateTraceSummaryProfile()],
        schemaVersion: TraceDefinitionSchemaVersion);

    /// <summary>Creates the span stream definition for a host-selected stream identity.</summary>
    public static DiagnosticRecordStreamDefinition CreateSpans(string streamId) => Definition(
        streamId,
        "elsa_open_telemetry_spans",
        [
            String(RecordFields.TraceId, required: true),
            String(RecordFields.SpanId, required: true),
            String(RecordFields.ParentSpanId),
            String(RecordFields.ResourceId, required: true),
            Int64(RecordFields.Status, required: true),
            Timestamp(RecordFields.StartTime, required: true, orderable: true),
            Timestamp(RecordFields.EndTime, required: true),
            String(RecordFields.Name, required: true, maxStringBytes: MaxNameBytes),
            String(
                RecordFields.OrderKey,
                required: true,
                orderable: true,
                maxStringBytes: MaxNameBytes,
                casePolicy: DiagnosticStringCasePolicy.Ordinal)
        ]);

    /// <summary>Creates the metric-point stream definition for a host-selected stream identity.</summary>
    public static DiagnosticRecordStreamDefinition CreateMetricPoints(string streamId) => Definition(
        streamId,
        "elsa_open_telemetry_metric_points",
        [
            String(RecordFields.InstrumentId, required: true),
            String(RecordFields.InstrumentName, required: true, maxStringBytes: MaxNameBytes),
            String(RecordFields.ResourceId, required: true),
            String(RecordFields.ServiceName),
            Timestamp(RecordFields.Timestamp, required: true, orderable: true),
            String(RecordFields.TraceId),
            String(RecordFields.SpanId),
            String(
                RecordFields.OrderKey,
                required: true,
                orderable: true,
                maxStringBytes: MaxNameBytes,
                casePolicy: DiagnosticStringCasePolicy.Ordinal)
        ]);

    /// <summary>Creates the telemetry-log stream definition for a host-selected stream identity.</summary>
    public static DiagnosticRecordStreamDefinition CreateLogs(string streamId) => Definition(
        streamId,
        "elsa_open_telemetry_logs",
        [
            String(RecordFields.ResourceId, required: true),
            String(RecordFields.ServiceName),
            Timestamp(RecordFields.Timestamp, required: true, orderable: true),
            String(RecordFields.SeverityText, required: true, maxStringBytes: MaxNameBytes),
            Int64(RecordFields.SeverityNumber),
            String(RecordFields.TraceId),
            String(RecordFields.SpanId),
            String(RecordFields.Body, required: true, maxStringBytes: MaxBodyBytes),
            String(
                RecordFields.OrderKey,
                required: true,
                orderable: true,
                maxStringBytes: MaxNameBytes,
                casePolicy: DiagnosticStringCasePolicy.Ordinal)
        ]);

    private static DiagnosticRecordStreamDefinition Definition(
        string streamId,
        string storageName,
        IReadOnlyList<DiagnosticFieldDefinition> fields,
        int maxPredicateValues = 8,
        IReadOnlyList<DiagnosticGroupReductionProfile>? groupReductionProfiles = null,
        int schemaVersion = CanonicalRecordSerializer.SchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        var definition = new DiagnosticRecordStreamDefinition(
            new(streamId),
            schemaVersion,
            storageName,
            fields,
            new(
                MaxBatchRecords: MaxRecordsPerSignalBatch,
                MaxPayloadBytes: 1_048_576,
                // SQL Server's portable diagnostic-record contract admits at most 128 UTF-8
                // bytes. Keep the shared stream definition within the narrowest provider bound.
                MaxRecordIdBytes: 128,
                MaxFieldsPerRecord: fields.Count,
                MaxQueryLimit: 5_000,
                MaxGroupedQueryInputRecords: MaxTraceRecordCapacity,
                MaxPredicateNodes: 32,
                // Non-trace streams keep the tighter eight-value budget because a telemetry-log
                // body can be 64 KiB. Traces opt into nine for their declared full-filter shape.
                MaxPredicateValues: maxPredicateValues,
                MaxJsonDepth: 64),
            MaxOperationClockSkew: TimeSpan.FromMinutes(5),
            AppendIdempotencyWindow: TimeSpan.FromHours(1),
            TrimIdempotencyWindow: TimeSpan.FromHours(1),
            GroupReductionProfiles: groupReductionProfiles);

        DiagnosticRecordStreamDefinitionValidator.ValidateAndThrow(definition);
        return definition;
    }

    private static DiagnosticGroupReductionProfile CreateTraceSummaryProfile() => new(
        TraceSummaryProfileName,
        RecordFields.TraceId,
        [
            new(RecordFields.TraceId, DiagnosticGroupReducerKind.FirstBy, RecordFields.TraceId,
                RecordFields.StartTime, DiagnosticSortDirection.Ascending, DiagnosticGroupFirstByTieBreak.CursorAscending),
            new(RecordFields.RootSpanId, DiagnosticGroupReducerKind.FirstBy, RecordFields.RootSpanId,
                RecordFields.StartTime, DiagnosticSortDirection.Ascending, DiagnosticGroupFirstByTieBreak.CursorAscending),
            new(RecordFields.Name, DiagnosticGroupReducerKind.FirstBy, RecordFields.Name,
                RecordFields.StartTime, DiagnosticSortDirection.Ascending, DiagnosticGroupFirstByTieBreak.CursorAscending),
            new(RecordFields.StartTime, DiagnosticGroupReducerKind.MinTimestamp, RecordFields.StartTime),
            new(RecordFields.EndTime, DiagnosticGroupReducerKind.MaxTimestamp, RecordFields.EndTime),
            new(RecordFields.Status, DiagnosticGroupReducerKind.MaxInt64, RecordFields.Status),
            new(RecordFields.ResourceId, DiagnosticGroupReducerKind.SetUnionString, RecordFields.ResourceId),
            new(RecordFields.ServiceName, DiagnosticGroupReducerKind.SetUnionString, RecordFields.ServiceName),
            new(RecordFields.WorkflowInstanceId, DiagnosticGroupReducerKind.SetUnionString, RecordFields.WorkflowInstanceId),
            new(RecordFields.SpanCount, DiagnosticGroupReducerKind.SumInt64, RecordFields.SpanCount)
        ],
        [
            new(RecordFields.TraceId, Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.Contains)),
            new(RecordFields.ResourceId, Set(DiagnosticPredicateOperator.Equal)),
            new(RecordFields.ServiceName, Set(DiagnosticPredicateOperator.Equal)),
            new(RecordFields.WorkflowInstanceId, Set(DiagnosticPredicateOperator.Contains)),
            new(RecordFields.Status, Set(DiagnosticPredicateOperator.Equal)),
            new(RecordFields.StartTime, Set(DiagnosticPredicateOperator.RangeInclusive)),
            new(RecordFields.Name, Set(DiagnosticPredicateOperator.Contains))
        ],
        Names(RecordFields.StartTime),
        MaxTake: 5_000,
        // Grouped trace queries reduce at most MaxTraceRecordCapacity newest raw records.
        // Each admitted trace record may contribute all 256 declared values, so this
        // bound is independent of retention timing and concurrent writers.
        MaxUnionValues: MaxTraceUnionValues);

    private static DiagnosticFieldDefinition String(
        string name,
        bool required = false,
        bool orderable = false,
        bool latestPerKey = false,
        bool multiple = false,
        int maxStringBytes = MaxIdentifierBytes,
        DiagnosticStringCasePolicy casePolicy = DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase) => new(
        name,
        DiagnosticFieldType.String,
        multiple ? DiagnosticFieldCardinality.Multiple : DiagnosticFieldCardinality.Scalar,
        Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In, DiagnosticPredicateOperator.Contains),
        IsRequired: required,
        IsOrderable: orderable,
        SupportsLatestPerKey: latestPerKey,
        // Diagnostics filters historically use ordinal case-insensitive matching. Groundwork's
        // versioned Unicode policy keeps that behavior provider-neutral without letting each
        // database select its own collation.
        CasePolicy: casePolicy,
        MaxValues: multiple ? MaxMultiValues : 1,
        MaxStringBytes: maxStringBytes);

    private static DiagnosticFieldDefinition Int64(string name, bool required = false) => new(
        name,
        DiagnosticFieldType.Int64,
        DiagnosticFieldCardinality.Scalar,
        Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In, DiagnosticPredicateOperator.RangeInclusive),
        IsRequired: required);

    private static DiagnosticFieldDefinition Timestamp(string name, bool required = false, bool orderable = false) => new(
        name,
        DiagnosticFieldType.Timestamp,
        DiagnosticFieldCardinality.Scalar,
        Set(DiagnosticPredicateOperator.Equal, DiagnosticPredicateOperator.In, DiagnosticPredicateOperator.RangeInclusive),
        IsRequired: required,
        IsOrderable: orderable);

    private static IReadOnlySet<DiagnosticPredicateOperator> Set(params DiagnosticPredicateOperator[] values) =>
        values.ToHashSet();

    private static IReadOnlySet<string> Names(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);
}
