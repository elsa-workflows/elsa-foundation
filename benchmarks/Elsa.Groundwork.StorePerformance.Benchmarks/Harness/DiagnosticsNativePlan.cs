using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Retained envelope for one diagnostics route. The command and provider plan are captured
/// together so admission can reparse both instead of trusting summary booleans in route metadata.</summary>
public sealed record DiagnosticsNativePlanArtifact(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("routeIdentity")] string RouteIdentity,
    [property: JsonPropertyName("tableName")] string TableName,
    [property: JsonPropertyName("indexName")] string IndexName,
    [property: JsonPropertyName("physicalIndexName")] string PhysicalIndexName,
    [property: JsonPropertyName("commandText")] string CommandText,
    [property: JsonPropertyName("nativePlan")] string NativePlan);

public sealed record DiagnosticsNativeRouteSpec(
    string RouteIdentity,
    string TableName,
    string IndexName,
    string? OrderColumn,
    string? PredicateColumn,
    int PhysicalCardinality,
    int FiniteLimit,
    bool StorageScopeRequired = false,
    bool Descending = true,
    IReadOnlyList<RuntimeNativeOrderTerm>? Ordering = null,
    IReadOnlyList<string>? NullableOrderingColumns = null)
{
    /// <summary>Relational Groundwork injects this equality into every scoped query. MongoDB isolates
    /// scopes with a provider-owned physical collection and therefore has no synthetic scope field.</summary>
    public IReadOnlyList<RuntimeNativeOrderTerm> EffectiveOrdering => Ordering ??
        (OrderColumn is null
            ? []
            : [new RuntimeNativeOrderTerm(OrderColumn, Descending ? RuntimeNativeOrderDirection.Descending : RuntimeNativeOrderDirection.Ascending)]);

    /// <summary>Whether the provider must retain an explicit null-rank term for this ordered column.
    /// A null value means the caller has not supplied nullability evidence, so admission stays
    /// conservative and requires null ranks for every term.</summary>
    public bool RequiresNullRank(string column) =>
        NullableOrderingColumns is null || NullableOrderingColumns.Contains(column, StringComparer.Ordinal);
}

public enum DiagnosticsTraceDetailOperationKind
{
    PrimaryKeyRead,
    BoundedOrderedQuery
}

/// <summary>One independently observed provider operation contributing to GetTraceAsync.</summary>
/// <remarks><see cref="PhysicalCardinality"/> is the total frozen physical table cardinality, not
/// the number of rows matching the selected trace. <see cref="PublicRowBound"/> is the caller-visible
/// capacity bound; <see cref="MaxInvocationCount"/> is the resulting finite page/fanout bound.</remarks>
public sealed record DiagnosticsTraceDetailConstituentSpec(
    string RouteIdentity,
    string TableName,
    string IndexName,
    string PredicateColumn,
    IReadOnlyList<RuntimeNativeOrderTerm> Ordering,
    DiagnosticsTraceDetailOperationKind OperationKind,
    int PhysicalCardinality,
    int FiniteLimit,
    int PublicRowBound,
    int MaxInvocationCount,
    bool StorageScopeRequired = false);

/// <summary>
/// Single source of truth for every scale-bearing diagnostics read executed by the frozen workload.
/// Trace detail is represented as an honest composite of bounded signal queries and primary-key reads;
/// primary-key constituents intentionally have no secondary index claim. No instrument route is listed because the public
/// <c>IOpenTelemetryStore</c> contract has no instrument-catalog query.
/// </summary>
public static partial class DiagnosticsNativePlanContract
{
    private const string BlockedPlanMarker = "blocked provider plan:";
    internal const string IndexSearchPlanClassification = "index-search";
    internal const string BoundedCatalogScanSortPlanClassification = "bounded-scan-sort";
    private const int BoundedResourceCardinality = 128;
    private const int BoundedResourceLimit = 127;
    private const string MongoStatusOnlyResourceIndex = "elsa_otel_resources_status";
    public const string GroundworkAdapter = "groundwork-v2";
    public const string EfAdapter = "ef-diagnostics-oracle";
    public const string EfCorrectnessOnlyRouteContract = "ef-correctness-only-unbounded-resource-routes";
    public const string BlockedRouteContract = "provider-native-routes-blocked";
    public const string GroundworkTable = "elsa_otel_resources_v2";
    public const string EfTable = "TelemetryResources";

    /// <summary>
    /// The one deliberately bounded scan exception in the diagnostics native-plan contract. The
    /// resource catalog is frozen at 128 physical rows and the public page is frozen at 127 rows;
    /// every other route remains an index-backed, no-sort claim. Eligibility belongs to the frozen
    /// workload shape rather than one optimizer: each provider must still prove its exact bounded
    /// scan, complete deterministic sort, finite limit, and absence of spill/materialization.
    /// </summary>
    internal static bool IsBoundedResourceRoute(
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification) =>
        provider is "postgresql" or "sqlserver" or "mongodb" &&
        string.Equals(adapter, GroundworkAdapter, StringComparison.Ordinal) &&
        specification.RouteIdentity is "resources-by-last-seen" or "resources-by-status" or "resources-by-service" &&
        string.Equals(specification.TableName, GroundworkTable, StringComparison.Ordinal) &&
        specification.IndexName == specification.RouteIdentity switch
        {
            "resources-by-last-seen" => "elsa_otel_resources_last_seen",
            "resources-by-status" => "elsa_otel_resources_status_last_seen",
            "resources-by-service" => "elsa_otel_resources_service_last_seen",
            _ => ""
        } &&
        string.Equals(specification.OrderColumn, "lastSeen", StringComparison.Ordinal) &&
        specification.StorageScopeRequired &&
        specification.Descending &&
        specification.PhysicalCardinality == BoundedResourceCardinality &&
        specification.FiniteLimit == BoundedResourceLimit;


    public static IReadOnlyList<DiagnosticsTraceDetailConstituentSpec> TraceDetailConstituents(string adapter)
    {
        if (!string.Equals(adapter, GroundworkAdapter, StringComparison.Ordinal))
            throw new PerformanceContractException($"Diagnostics native-plan admission does not support adapter '{adapter}'.");

        var pageCount = checked((DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream +
                                 DiagnosticsDurableHistoryWorkload.QueryLimit - 1) /
                                DiagnosticsDurableHistoryWorkload.QueryLimit);
        var resourceFanout = Math.Min(5_000, DiagnosticsDurableHistoryWorkload.ResourceCount);
        return [
                new(
                    "trace-detail/summary-by-trace-key",
                    "elsa_otel_trace_summaries_v3",
                    "",
                    "traceKey",
                    [],
                    DiagnosticsTraceDetailOperationKind.PrimaryKeyRead,
                    DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                    1,
                    1,
                    1,
                    true),
                new(
                    "trace-detail/spans-by-trace-key-start-id",
                    "elsa_otel_spans_v2",
                    "elsa_otel_spans_trace_detail",
                    "traceKey",
                    [
                        new("startTime", RuntimeNativeOrderDirection.Ascending),
                        new("spanId", RuntimeNativeOrderDirection.Ascending),
                        new("sequence", RuntimeNativeOrderDirection.Ascending)
                    ],
                    DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery,
                    DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                    DiagnosticsDurableHistoryWorkload.QueryLimit,
                    DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                    pageCount,
                    true),
                new(
                    "trace-detail/logs-by-trace-key-timestamp-id",
                    "elsa_otel_logs_v2",
                    "elsa_otel_logs_trace_detail",
                    "traceKey",
                    [
                        new("timestamp", RuntimeNativeOrderDirection.Ascending),
                        new("id", RuntimeNativeOrderDirection.Ascending),
                        new("sequence", RuntimeNativeOrderDirection.Ascending)
                    ],
                    DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery,
                    DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                    DiagnosticsDurableHistoryWorkload.QueryLimit,
                    DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                    pageCount,
                    true),
                new(
                    "trace-detail/resources-by-id",
                    "elsa_otel_resources_v2",
                    "",
                    "id",
                    [],
                    DiagnosticsTraceDetailOperationKind.PrimaryKeyRead,
                    DiagnosticsDurableHistoryWorkload.ResourceCount,
                    1,
                    DiagnosticsDurableHistoryWorkload.ResourceCount,
                    resourceFanout,
                    true)
            ];
    }

    /// <summary>
    /// Identifies provider-plan failures that are a valid blocked-route outcome. Contract and
    /// command-binding failures remain hard failures: capture must not turn a changed table, index, or
    /// predicate into a blocked route and thereby hide schema drift.
    /// </summary>
    internal static bool IsExpectedBlockedPlanFailure(PerformanceContractException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Message.Contains(BlockedPlanMarker, StringComparison.Ordinal);
    }

    /// <summary>Maps the fixed provider-plan failure vocabulary to value-free diagnostic codes. The
    /// exception text is deliberately not retained because it may include provider command details.</summary>
    internal static string BlockedPlanReasonCode(PerformanceContractException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var marker = exception.Message.IndexOf(BlockedPlanMarker, StringComparison.Ordinal);
        if (marker < 0)
            return "native-plan.blocked";

        var detail = exception.Message[(marker + BlockedPlanMarker.Length)..];
        return detail switch
        {
            _ when detail.Contains("sort or materialization spill", StringComparison.Ordinal) => "native-plan.sort-or-materialization-spill",
            _ when detail.Contains("sequential scan", StringComparison.Ordinal) => "native-plan.sequential-scan",
            _ when detail.Contains("explicit collection scan or sort", StringComparison.Ordinal) => "native-plan.scan-sort-shape",
            _ when detail.Contains("collection scan", StringComparison.Ordinal) => "native-plan.collection-scan",
            _ when detail.Contains("index scan", StringComparison.Ordinal) => "native-plan.index-scan",
            _ when detail.Contains("complete effective ordering", StringComparison.Ordinal) => "native-plan.ordering-mismatch",
            _ when detail.Contains("finite page limit", StringComparison.Ordinal) => "native-plan.limit-mismatch",
            _ => "native-plan.blocked"
        };
    }

    private static PerformanceContractException BlockedPlan(
        DiagnosticsNativeRouteSpec specification,
        string detail) =>
        new($"Diagnostics route '{specification.RouteIdentity}' {BlockedPlanMarker} {detail}.");

    public static DiagnosticsNativeRouteSpec For(string adapter, string route)
    {
        if (adapter is not (GroundworkAdapter or EfAdapter))
            throw new PerformanceContractException($"Diagnostics native-plan admission does not support adapter '{adapter}'.");

        DiagnosticsNativeRouteSpec Specification(
            string table,
            string index,
            string? order,
            string? predicate,
            int cardinality,
            bool scope = false) =>
            new(
                route,
                table,
                index,
                order,
                predicate,
                cardinality,
                DiagnosticsDurableHistoryWorkload.NativeRouteLimits[route],
                scope,
                route != "structured-log-replay",
                route switch
                {
                    "resources-by-last-seen" or "resources-by-status" or "resources-by-service" =>
                    [
                        new("lastSeen", RuntimeNativeOrderDirection.Descending),
                        new("idOrderKey", RuntimeNativeOrderDirection.Ascending),
                        new("id", RuntimeNativeOrderDirection.Ascending)
                    ],
                    "traces-by-last-seen" => [new("startTime", RuntimeNativeOrderDirection.Descending), new("traceKey", RuntimeNativeOrderDirection.Ascending)],
                    "metrics-by-last-seen" or "logs-by-last-seen" =>
                    [
                        new("timestamp", RuntimeNativeOrderDirection.Descending),
                        new("id", RuntimeNativeOrderDirection.Ascending),
                        new("sequence", RuntimeNativeOrderDirection.Ascending)
                    ],
                    "structured-log-recent" => [new("sequence", RuntimeNativeOrderDirection.Descending)],
                    "structured-log-replay" => [new("sequence", RuntimeNativeOrderDirection.Ascending)],
                    _ => []
                },
                adapter == GroundworkAdapter ? [] : null);

        return route switch
        {
            "resources-by-last-seen" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_LastSeen", "LastSeen", null, DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "elsa_otel_resources_last_seen", "lastSeen", null, DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            "resources-by-status" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_Status", "LastSeen", "Status", DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "elsa_otel_resources_status_last_seen", "lastSeen", "status", DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            // The service and ID identities keep the complete seek/order key inside Groundwork's
            // strict portable key budget without shortening the public resource fields.
            "resources-by-service" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_ServiceName", "LastSeen", "ServiceName", DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "elsa_otel_resources_service_last_seen", "lastSeen", "serviceNameKey", DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            "traces-by-last-seen" => adapter == EfAdapter
                ? Specification("TelemetryTraces", "IX_PersistedTelemetryTrace_StartTime", "StartTime", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start", "startTime", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            // GetTraceAsync is admitted through TraceDetailConstituents below; this top-level
            // specification remains empty so it cannot accidentally become a synthetic index claim.
            "trace-detail" => adapter == EfAdapter
                ? Specification("TelemetryTraces", "", null, "TraceId", DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_trace_summaries_v3", "", null, "traceKey", DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "metrics-by-last-seen" => adapter == EfAdapter
                ? Specification("MetricPoints", "", "Timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_metric_points_v2", "elsa_otel_metric_points_timestamp", "timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "logs-by-last-seen" => adapter == EfAdapter
                ? Specification("OtlpLogRecords", "", "Timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_logs_v2", "elsa_otel_logs_timestamp", "timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "structured-log-recent" => adapter == EfAdapter
                ? Specification("StructuredLogEntries", "IX_PersistedStructuredLogEntry_Sequence", "Id", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream)
                : Specification("elsa_structured_logs", "elsa_structured_logs_sequence_order", "sequence", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream, true),
            "structured-log-replay" => adapter == EfAdapter
                ? Specification("StructuredLogEntries", "IX_PersistedStructuredLogEntry_Sequence", "Id", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream)
                : Specification("elsa_structured_logs", "elsa_structured_logs_sequence_order", "sequence", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream, true),
            _ => throw new PerformanceContractException($"Diagnostics native-plan admission does not support route '{route}'.")
        };
    }

    public static string ExpectedPhysicalIndexName(string provider, DiagnosticsNativeRouteSpec specification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(specification);
        if (string.IsNullOrWhiteSpace(specification.IndexName))
            return string.Empty;
        return GroundworkPhysicalIndexNames.For(provider, specification.TableName, specification.IndexName);
    }

    /// <summary>Returns whether the retained command must prove a synthetic scope predicate. MongoDB
    /// proves the same isolation boundary through its physical scoped collection name.</summary>
    public static bool ExpectedStorageScopePredicate(string provider, DiagnosticsNativeRouteSpec specification) =>
        ExpectedStorageScopePredicate(provider, specification.StorageScopeRequired);

    /// <summary>Returns whether a route with the supplied scope requirement must expose a synthetic
    /// scope predicate for the named provider.</summary>
    public static bool ExpectedStorageScopePredicate(string provider, bool storageScopeRequired) =>
        storageScopeRequired && !string.Equals(provider, "mongodb", StringComparison.Ordinal);

    // Groundwork's page executor fetches one extra row to determine continuation. FiniteLimit
    // and retained evidence counts remain the public page bound, not this native fetch ceiling.
    internal static int ExpectedNativeFetchLimit(DiagnosticsNativeRouteSpec specification) =>
        specification.StorageScopeRequired ? checked(specification.FiniteLimit + 1) : specification.FiniteLimit;

    internal static bool IsOrdinalStringOrderColumn(string column) =>
        column is "id" or "idOrderKey" or "traceKey" or "spanId";
}
