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
public static class DiagnosticsNativePlanContract
{
    private const string BlockedPlanMarker = "blocked provider plan:";
    internal const string IndexSearchPlanClassification = "index-search";
    internal const string BoundedCatalogScanSortPlanClassification = "bounded-scan-sort";
    private const int BoundedResourceCardinality = 128;
    private const int BoundedResourceLimit = 127;
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

    /// <summary>Classifies only the exact bounded resource-catalog scan/sort exception; unknown or malformed
    /// plans fall back to the strict index-search classification and are rejected by validation.</summary>
    internal static string ClassifyPlan(
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification,
        string nativePlan) =>
        IsBoundedResourceRoute(provider, adapter, specification) &&
        IsBoundedScanSortShape(provider, nativePlan, specification)
            ? BoundedCatalogScanSortPlanClassification
            : IndexSearchPlanClassification;

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

    public static void ValidateEnvelope(
        string provider,
        string adapter,
        NativeRouteEvidence route,
        string path)
    {
        DiagnosticsNativePlanArtifact artifact;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            ArtifactStore.RejectDuplicateProperties(document.RootElement);
            artifact = JsonSerializer.Deserialize<DiagnosticsNativePlanArtifact>(document.RootElement.GetRawText())
                ?? throw new PerformanceContractException("Diagnostics native-plan envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Diagnostics native-plan envelope is invalid: {exception.Message}");
        }

        var specification = For(adapter, route.RouteIdentity);
        if (artifact.SchemaVersion != 1 || artifact.Provider != provider || artifact.Adapter != adapter ||
            artifact.RouteIdentity != route.RouteIdentity || artifact.TableName != specification.TableName ||
            artifact.IndexName != specification.IndexName ||
            artifact.PhysicalIndexName != ExpectedPhysicalIndexName(provider, specification) ||
            string.IsNullOrWhiteSpace(artifact.CommandText) ||
            string.IsNullOrWhiteSpace(artifact.NativePlan))
            throw new PerformanceContractException(
                $"Diagnostics native-plan envelope does not bind route '{route.RouteIdentity}' to its exact provider, adapter, table, and index.");

        if (route.PhysicalCardinality != specification.PhysicalCardinality ||
            route.FiniteLimit != specification.FiniteLimit ||
            route.MaterializedCandidateCount != specification.FiniteLimit ||
            route.HasStorageScopePredicate != ExpectedStorageScopePredicate(provider, specification) ||
            route.HasRoutePredicate != (specification.PredicateColumn is not null))
            throw new PerformanceContractException(
                $"Diagnostics native-plan route '{route.RouteIdentity}' has unbound cardinality, finite-page, or predicate facts.");

        var physicalIndexName = ExpectedPhysicalIndexName(provider, specification);
        if (route.IndexName != physicalIndexName)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' does not bind its provider-owned physical index name.");

        if (!string.Equals(route.PlanClassification, IndexSearchPlanClassification, StringComparison.Ordinal) &&
            !string.Equals(route.PlanClassification, BoundedCatalogScanSortPlanClassification, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' has an unsupported native-plan classification '{route.PlanClassification}'.");

        var boundedCatalogScan = string.Equals(
            route.PlanClassification,
            BoundedCatalogScanSortPlanClassification,
            StringComparison.Ordinal);
        if (boundedCatalogScan && !IsBoundedResourceRoute(provider, adapter, specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' may use '{BoundedCatalogScanSortPlanClassification}' only for the frozen resource catalog routes.");

        IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)>? mongoCommandOrdering = null;
        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
            mongoCommandOrdering = ValidateMongoCommand(artifact.CommandText, specification, artifact.NativePlan);
        else
            ValidateSqlCommand(provider, artifact.CommandText, specification);

        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "postgresql":
                if (boundedCatalogScan)
                    ValidatePostgreSqlBoundedScanSortPlan(artifact.NativePlan, specification);
                else
                    ValidatePostgreSqlPlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "sqlserver":
                if (boundedCatalogScan)
                    ValidateSqlServerBoundedScanSortPlan(artifact.NativePlan, specification);
                else
                    ValidateSqlServerPlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "mongodb":
                if (boundedCatalogScan)
                    ValidateMongoBoundedScanSortPlan(
                        artifact.NativePlan,
                        specification,
                        mongoCommandOrdering ?? throw new PerformanceContractException(
                            $"Diagnostics route '{specification.RouteIdentity}' MongoDB command ordering was not retained for plan validation."));
                else
                    ValidateMongoPlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            default:
                throw new PerformanceContractException($"Diagnostics native-plan admission does not support provider '{provider}'.");
        }
    }

    public static void ValidateTraceDetailConstituent(
        string provider,
        string adapter,
        DiagnosticsTraceDetailConstituentEvidence evidence,
        string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(evidence);
        var specification = TraceDetailConstituents(adapter).SingleOrDefault(item =>
            string.Equals(item.RouteIdentity, evidence.RouteIdentity, StringComparison.Ordinal)) ??
            throw new PerformanceContractException($"Diagnostics trace-detail evidence names unknown constituent '{evidence.RouteIdentity}'.");
        var hasBoundedCounts = specification.OperationKind switch
        {
            DiagnosticsTraceDetailOperationKind.PrimaryKeyRead =>
                evidence.ObservedCommandCount > 0 &&
                evidence.ObservedCommandCount <= specification.MaxInvocationCount &&
                evidence.MaterializedCandidateCount == evidence.ObservedCommandCount &&
                evidence.MaterializedCandidateCount <= evidence.PublicRowBound,
            DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery =>
                evidence.ObservedCommandCount == specification.MaxInvocationCount &&
                evidence.MaterializedCandidateCount == evidence.PublicRowBound,
            _ => false
        };

        if (evidence.PhysicalCardinality != specification.PhysicalCardinality ||
            evidence.FiniteLimit != specification.FiniteLimit ||
            evidence.PublicRowBound != specification.PublicRowBound ||
            evidence.MaxInvocationCount != specification.MaxInvocationCount ||
            !hasBoundedCounts ||
            evidence.MaterializedCandidateCount > checked(evidence.FiniteLimit * evidence.ObservedCommandCount) ||
            evidence.HasStorageScopePredicate !=
                (specification.StorageScopeRequired && !string.Equals(provider, "mongodb", StringComparison.Ordinal)) ||
            evidence.HasRoutePredicate != true ||
            string.IsNullOrWhiteSpace(evidence.CommandText))
            throw new PerformanceContractException(
                $"Diagnostics trace-detail constituent '{evidence.RouteIdentity}' has unbound cardinality, limit, fanout, scope, or predicate facts.");

        if (specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead)
        {
            if (!string.IsNullOrEmpty(evidence.RawPlanReference) ||
                !string.IsNullOrEmpty(evidence.RawPlanSha256) ||
                !string.IsNullOrEmpty(evidence.PhysicalIndexName) ||
                !string.Equals(evidence.PlanClassification, "primary-key-read", StringComparison.Ordinal) ||
                evidence.Pages is { Count: > 0 })
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail point read '{evidence.RouteIdentity}' must not claim a secondary index or explain artifact.");
            ValidatePointCommand(provider, evidence.CommandText, specification);
            return;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(evidence.RawPlanReference) ||
            string.IsNullOrWhiteSpace(evidence.RawPlanSha256) ||
            string.IsNullOrWhiteSpace(evidence.PhysicalIndexName) ||
            !string.Equals(evidence.PlanClassification, IndexSearchPlanClassification, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Diagnostics trace-detail query '{evidence.RouteIdentity}' must retain its provider-native plan artifact.");

        var artifact = ReadArtifact(path);
        var expectedPhysicalIndex = ExpectedPhysicalIndexName(provider, new DiagnosticsNativeRouteSpec(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering.First().Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering));
        if (artifact.SchemaVersion != 1 || artifact.Provider != provider || artifact.Adapter != adapter ||
            artifact.RouteIdentity != evidence.RouteIdentity || artifact.TableName != specification.TableName ||
            artifact.IndexName != specification.IndexName || artifact.PhysicalIndexName != expectedPhysicalIndex ||
            evidence.PhysicalIndexName != expectedPhysicalIndex || string.IsNullOrWhiteSpace(artifact.CommandText) ||
            string.IsNullOrWhiteSpace(artifact.NativePlan) || !string.Equals(evidence.CommandText, artifact.CommandText, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Diagnostics trace-detail constituent '{evidence.RouteIdentity}' does not bind its exact provider, table, or physical index.");

        var routeSpecification = new DiagnosticsNativeRouteSpec(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering.First().Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering);
        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
            ValidateMongoCommand(artifact.CommandText, routeSpecification, artifact.NativePlan);
        else
            ValidateSqlCommand(provider, artifact.CommandText, routeSpecification);

        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(artifact.NativePlan, routeSpecification, expectedPhysicalIndex);
                break;
            case "postgresql":
                ValidatePostgreSqlPlan(artifact.NativePlan, routeSpecification, expectedPhysicalIndex);
                break;
            case "sqlserver":
                ValidateSqlServerPlan(artifact.NativePlan, routeSpecification, expectedPhysicalIndex);
                break;
            case "mongodb":
                ValidateMongoPlan(artifact.NativePlan, routeSpecification, expectedPhysicalIndex);
                break;
            default:
                throw new PerformanceContractException($"Diagnostics native-plan admission does not support provider '{provider}'.");
        }
    }

    private static DiagnosticsNativePlanArtifact ReadArtifact(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            ArtifactStore.RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<DiagnosticsNativePlanArtifact>(document.RootElement.GetRawText()) ??
                throw new PerformanceContractException("Diagnostics native-plan envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Diagnostics native-plan envelope is invalid: {exception.Message}");
        }
    }

    private static void ValidatePointCommand(
        string provider,
        string command,
        DiagnosticsTraceDetailConstituentSpec specification)
    {
        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
        {
            ValidateMongoPointCommand(command, specification);
            return;
        }

        var normalized = NormalizeSqlCommand(command);
        if (normalized.Contains(" OR ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" ORDER BY ", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(normalized, @"\bOFFSET\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(normalized, @"\b(?:LIMIT|TOP)\s*\(?\s*(?!1\b)\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(specification.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics point read '{specification.RouteIdentity}' is not an unsorted primary-key lookup.");
        var where = Regex.Match(
            normalized,
            @"\bWHERE\s+(?<where>.*?)(?:;|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline).Groups["where"].Value.Trim();
        var required = new[] { "__groundwork_scope", specification.PredicateColumn };
        if (!ContainsOnlyExactEqualityPredicates(where, required, allowNullGuards: false))
            throw new PerformanceContractException($"Diagnostics point read '{specification.RouteIdentity}' must contain only scope and its exact key predicate.");
    }

    private static void ValidateMongoPointCommand(
        string command,
        DiagnosticsTraceDetailConstituentSpec specification)
    {
        var collection = RequireMongoPointCollection(command);
        ValidateMongoPhysicalCollection(collection, specification);
    }

    internal static string RequireMongoPointCollection(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        try
        {
            using var document = JsonDocument.Parse(command);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("collection", out var collection) ||
                collection.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("filter", out var filter) ||
                filter.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("limit", out var limit) ||
                limit.ValueKind != JsonValueKind.Number ||
                !limit.TryGetInt32(out var limitValue) ||
                limitValue != 1)
                throw new PerformanceContractException(
                    "Diagnostics MongoDB point read must retain only its physical collection, redacted _id equality, and limit one.");

            var filterProperties = filter.EnumerateObject().ToArray();
            if (filterProperties.Length != 1 ||
                !string.Equals(filterProperties[0].Name, "_id", StringComparison.Ordinal) ||
                filterProperties[0].Value.ValueKind != JsonValueKind.Object)
                throw new PerformanceContractException(
                    "Diagnostics MongoDB point read must retain only its redacted _id equality.");
            var equalityProperties = filterProperties[0].Value.EnumerateObject().ToArray();
            if (equalityProperties.Length != 1 ||
                !string.Equals(equalityProperties[0].Name, "$eq", StringComparison.Ordinal) ||
                equalityProperties[0].Value.ValueKind != JsonValueKind.String ||
                !string.Equals(equalityProperties[0].Value.GetString(), "<redacted>", StringComparison.Ordinal))
                throw new PerformanceContractException(
                    "Diagnostics MongoDB point read must redact its exact _id value.");
            return collection.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"Diagnostics MongoDB point read has invalid command JSON: {exception.Message}");
        }
    }

    private static void ValidateSqlContinuationPredicate(string where, DiagnosticsNativeRouteSpec specification)
    {
        var parameter = @"(?:@\w+|\?|\$\d+)";
        var baseColumns = new[] { "__groundwork_scope", specification.PredicateColumn! };
        var keyset = where;
        foreach (var column in baseColumns)
        {
            var predicate = $@"\b{Regex.Escape(column)}\b\s*=\s*{parameter}";
            if (Regex.Matches(keyset, predicate, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count != 1)
                throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' continuation does not retain its exact scope and route predicates.");
            keyset = Regex.Replace(keyset, predicate, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            keyset = Regex.Replace(
                keyset,
                $@"\b{Regex.Escape(column)}\b\s+IS\s+NOT\s+NULL",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (specification.EffectiveOrdering.Count > 1 &&
            !keyset.Contains(" OR ", StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' continuation does not retain its complete ascending keyset predicate.");

        var remainder = keyset;
        foreach (var term in specification.EffectiveOrdering)
        {
            var column = Regex.Escape(term.Column);
            var after = term.Direction == RuntimeNativeOrderDirection.Ascending ? ">" : "<";
            if (!Regex.IsMatch(
                    keyset,
                    $@"\b{column}\b\s*{Regex.Escape(after)}\s*{parameter}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' continuation does not retain its complete ascending keyset predicate.");

            remainder = Regex.Replace(remainder, $@"\b{column}\b\s+IS\s+NOT\s+NULL", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            remainder = Regex.Replace(remainder, $@"\b{column}\b\s+IS\s+NULL", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            remainder = Regex.Replace(remainder, $@"\b{column}\b\s*=\s*{parameter}", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            remainder = Regex.Replace(remainder, $@"\b{column}\b\s*{Regex.Escape(after)}\s*{parameter}", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        remainder = Regex.Replace(remainder, @"\b(?:AND|OR)\b|[();\s]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!string.IsNullOrWhiteSpace(remainder) ||
            keyset.Contains(" OR 1", StringComparison.OrdinalIgnoreCase) ||
            keyset.Contains("CASE", StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' continuation contains an unrecognized keyset expression.");
    }

    private static void ValidateSqlCommand(
        string provider,
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        if (string.Equals(provider, "postgresql", StringComparison.Ordinal) &&
            !PostgreSqlOrdinalCollationsMatch(command, specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' command does not bind its exact PostgreSQL ordinal collation.");
        var normalized = NormalizeSqlCommand(command);

        if (!Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(specification.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(
                normalized,
                $@"\b(?:LIMIT\s+(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)|FETCH\s+(?:FIRST|NEXT)\s+(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)\s+ROWS?|TOP\s*\(?\s*(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)\s*\)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its exact table, descending order, and finite page.");

        ValidateSqlOrdering(provider, normalized, specification);

        var continuation = specification.RouteIdentity.StartsWith("trace-detail/", StringComparison.Ordinal) &&
                           normalized.Contains(" OR ", StringComparison.OrdinalIgnoreCase);
        if ((!continuation && normalized.Contains(" OR ", StringComparison.OrdinalIgnoreCase)) ||
            !Regex.IsMatch(normalized, @"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && specification.StorageScopeRequired)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command contains an unbound boolean predicate.");

        var where = Regex.Match(normalized, @"\bWHERE\s+(?<where>.*?)(?:\bORDER\s+BY\b|\bLIMIT\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["where"].Value.Trim();
        if (Regex.IsMatch(where, @"\b(?:CASE|SELECT|LOWER|UPPER|COALESCE|CAST|SUBSTR|DATE|DATETIME)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(where, @"\bOR\s+(?:1\s*=\s*1|TRUE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command contains a computed or tautological predicate.");
        if (continuation)
        {
            ValidateSqlContinuationPredicate(where, specification);
            return;
        }

        if (string.Equals(specification.RouteIdentity, "structured-log-replay", StringComparison.Ordinal))
        {
            if (!ContainsOnlyExactReplayRangePredicates(where, specification.StorageScopeRequired))
                throw new PerformanceContractException(
                    "Diagnostics route 'structured-log-replay' command must retain only its exact scope and bounded sequence window.");
            return;
        }

        var requiredAtoms = new List<string>();
        if (specification.StorageScopeRequired)
            requiredAtoms.Add("__groundwork_scope");
        if (specification.PredicateColumn is not null)
            requiredAtoms.Add(specification.PredicateColumn);
        if (!ContainsOnlyExactEqualityPredicates(where, requiredAtoms, allowNullGuards: true))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command must contain only its exact equality predicates and no extra conditions.");
    }

    private static bool PostgreSqlOrdinalCollationsMatch(
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        var order = Regex.Match(
            command,
            @"\bORDER\s+BY\s+(?<order>.*?)(?:\bLIMIT\b|\bOFFSET\b|\bFETCH\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)
            .Groups["order"].Value;
        var ordinalColumns = specification.EffectiveOrdering
            .Where(term => IsOrdinalStringOrderColumn(term.Column))
            .Select(term => term.Column)
            .ToArray();
        var collations = Regex.Matches(
            order,
            @"\bCOLLATE\s+(?:""(?<name>[^""]+)""|(?<name>[A-Za-z_][A-Za-z0-9_.]*))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (collations.Any(match => !string.Equals(match.Groups["name"].Value, "C", StringComparison.Ordinal)))
            return false;

        var occurrences = ordinalColumns.Select(column => Regex.Matches(
                order,
                $@"(?<![A-Za-z0-9_])""?{Regex.Escape(column)}""?(?![A-Za-z0-9_])\s+COLLATE\s+(?:""C""|C\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count)
            .ToArray();
        return occurrences.All(count => count is 1 or 2) && occurrences.Sum() == collations.Count;
    }

    private static string NormalizeSqlCommand(string command)
    {
        var normalized = command.Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        return Regex.Replace(
            normalized,
            @"\s+COLLATE\s+[A-Za-z_][A-Za-z0-9_.]*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    private static bool ContainsOnlyExactEqualityPredicates(
        string where,
        IReadOnlyList<string> requiredColumns,
        bool allowNullGuards)
    {
        const string parameter = @"(?:@\w+|\?|\$\d+)";
        if (requiredColumns.Any(column =>
                Regex.Matches(where, $@"\b{Regex.Escape(column)}\b\s*=\s*{parameter}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count != 1))
            return false;

        var remainder = where;
        foreach (var column in requiredColumns)
        {
            if (allowNullGuards)
            {
                remainder = Regex.Replace(
                    remainder,
                    $@"\b{Regex.Escape(column)}\b\s+IS\s+NOT\s+NULL",
                    string.Empty,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            remainder = Regex.Replace(
                remainder,
                $@"\b{Regex.Escape(column)}\b\s*=\s*{parameter}",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        remainder = Regex.Replace(remainder, @"\bAND\b|[();\s]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return remainder.Length == 0;
    }

    private static bool ContainsOnlyExactReplayRangePredicates(string where, bool requireStorageScope)
    {
        const string parameter = @"(?:@\w+|\?|\$\d+)";
        var required = new List<string>
        {
            $@"\bsequence\b\s*>\s*{parameter}",
            $@"\bsequence\b\s*<=\s*{parameter}"
        };
        if (requireStorageScope)
            required.Add($@"\b__groundwork_scope\b\s*=\s*{parameter}");
        if (required.Any(pattern =>
                Regex.Matches(where, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count != 1))
            return false;

        var remainder = Regex.Replace(
            where,
            @"\b(?:sequence|__groundwork_scope)\b\s+IS\s+NOT\s+NULL",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var pattern in required)
            remainder = Regex.Replace(remainder, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        remainder = Regex.Replace(remainder, @"\bAND\b|[();\s]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return remainder.Length == 0;
    }

    private static void ValidateSqlOrdering(
        string provider,
        string command,
        DiagnosticsNativeRouteSpec specification)
    {
        var match = Regex.Match(
            command,
            @"\bORDER\s+BY\s+(?<order>.*?)(?:\bLIMIT\b|\bOFFSET\b|\bFETCH\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var terms = SplitTopLevelSqlTerms(match.Groups["order"].Value);
        if (!SqlOrderingMatches(provider, terms, specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' command does not bind its complete ordered term list.");
    }

    private static bool SqlOrderingMatches(
        string provider,
        IReadOnlyList<string> terms,
        DiagnosticsNativeRouteSpec specification)
    {
        var index = 0;
        foreach (var expected in specification.EffectiveOrdering)
        {
            if (index < terms.Count && IsSqlNullRank(terms[index]))
            {
                if (!SqlNullRankMatches(terms[index], expected.Column))
                    return false;
                index++;
            }

            if (index >= terms.Count ||
                !TryParseSqlOrderValue(provider, terms[index].Trim().TrimEnd(';'), out var ordered) ||
                ordered is null ||
                !string.Equals(ordered.Column, expected.Column, StringComparison.OrdinalIgnoreCase) ||
                ordered.Direction != expected.Direction)
                return false;
            index++;

            if (!string.Equals(provider, "sqlserver", StringComparison.Ordinal) ||
                !IsOrdinalStringOrderColumn(expected.Column))
                continue;

            var requiresLength = IsBoundedResourceRoute("sqlserver", GroundworkAdapter, specification);
            if (index >= terms.Count || !TryParseSqlServerLengthOrder(terms[index], out var lengthOrder))
            {
                if (requiresLength)
                    return false;
                continue;
            }
            if (lengthOrder is null ||
                !string.Equals(lengthOrder.Column, expected.Column, StringComparison.OrdinalIgnoreCase) ||
                lengthOrder.Direction != expected.Direction)
                return false;
            index++;
        }

        return index == terms.Count;
    }

    private static bool IsSqlNullRank(string term) => Regex.IsMatch(
        term.Trim().TrimEnd(';'),
        @"^CASE\s+WHEN\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool SqlNullRankMatches(string term, string column)
    {
        var match = Regex.Match(
            term.Trim().TrimEnd(';'),
            @"^CASE\s+WHEN\s+\(*\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<column>[A-Za-z_][A-Za-z0-9_]*)\s*\)*\s+IS\s+NULL\s+THEN\s+(?:0\s+ELSE\s+1|1\s+ELSE\s+0)\s+END\s+ASC$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success &&
               string.Equals(match.Groups["column"].Value, column, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSqlOrderValue(
        string provider,
        string term,
        out RuntimeNativeOrderTerm? ordered)
    {
        var simple = Regex.Match(
            term,
            @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<column>[A-Za-z_][A-Za-z0-9_]*)\s+(?<direction>ASC|DESC)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (simple.Success)
        {
            ordered = new RuntimeNativeOrderTerm(
                simple.Groups["column"].Value,
                string.Equals(simple.Groups["direction"].Value, "DESC", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeNativeOrderDirection.Descending
                    : RuntimeNativeOrderDirection.Ascending);
            return true;
        }
        if (string.Equals(provider, "postgresql", StringComparison.Ordinal) &&
            TryParsePostgreSqlOrdinalOrder(term, out ordered))
            return true;

        ordered = default;
        return false;
    }

    private static bool TryParsePostgreSqlOrdinalOrder(
        string term,
        out RuntimeNativeOrderTerm? ordered)
    {
        var match = Regex.Match(
            term,
            @"^(?<expression>.+)\s+(?<direction>ASC|DESC)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!match.Success)
        {
            ordered = default;
            return false;
        }

        var actual = Regex.Replace(match.Groups["expression"].Value.Trim(), @"\s+", " ");
        foreach (var column in new[] { "id", "idOrderKey", "traceKey", "spanId" })
        {
            var expected = Regex.Replace(PostgreSqlOrdinalExpression("(" + column + ")"), @"\s+", " ");
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                continue;
            ordered = new RuntimeNativeOrderTerm(
                column,
                string.Equals(match.Groups["direction"].Value, "DESC", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeNativeOrderDirection.Descending
                    : RuntimeNativeOrderDirection.Ascending);
            return true;
        }

        ordered = default;
        return false;
    }

    private static string PostgreSqlOrdinalExpression(string expression) =>
        "COALESCE((SELECT string_agg(CASE WHEN ascii(chars.ch) <= 65535 THEN " +
        "lpad(to_hex(ascii(chars.ch)), 4, '0') ELSE " +
        "lpad(to_hex(55296 + ((ascii(chars.ch) - 65536) >> 10)), 4, '0') || " +
        "lpad(to_hex(56320 + ((ascii(chars.ch) - 65536) & 1023)), 4, '0') END, '' ORDER BY chars.ord) " +
        "FROM unnest(string_to_array(" + expression + ", NULL)) WITH ORDINALITY AS chars(ch, ord)), '')";

    private static bool TryParseSqlServerLengthOrder(
        string term,
        out RuntimeNativeOrderTerm? ordered)
    {
        var match = Regex.Match(
            term,
            @"^DATALENGTH\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<column>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s+(?<direction>ASC|DESC)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        ordered = match.Success
            ? new RuntimeNativeOrderTerm(
                match.Groups["column"].Value,
                string.Equals(match.Groups["direction"].Value, "DESC", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeNativeOrderDirection.Descending
                    : RuntimeNativeOrderDirection.Ascending)
            : default;
        return match.Success;
    }

    private static IReadOnlyList<string> SplitTopLevelSqlTerms(string value)
    {
        var terms = new List<string>();
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\'' && quoted && index + 1 < value.Length && value[index + 1] == '\'')
            {
                index++;
                continue;
            }
            if (character == '\'')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
                continue;
            if (character == '(')
                depth++;
            else if (character == ')' && depth > 0)
                depth--;
            else if (character == ',' && depth == 0)
            {
                terms.Add(value[start..index].Trim());
                start = index + 1;
            }
        }
        if (quoted || depth != 0)
            return [];
        if (start < value.Length)
            terms.Add(value[start..].Trim());
        return terms.Where(term => term.Length != 0).ToArray();
    }

    private static void ValidateSqlitePlan(string plan, DiagnosticsNativeRouteSpec specification, string physicalIndexName)
    {
        var lines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(line => Regex.IsMatch(
                line,
                @"\b(?:USE\s+)?TEMP(?:ORARY)?\s+B[- ]TREE\b|\bMATERIAL(?:IZE|IZED|IZATION)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw BlockedPlan(specification, "SQLite sort or materialization spill");
        if (lines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\bSCAN\s+(?:{System.Text.RegularExpressions.Regex.Escape(specification.TableName)}|{System.Text.RegularExpressions.Regex.Escape(specification.TableName.Trim('"'))})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
                             !line.Contains("USING INDEX " + physicalIndexName, StringComparison.OrdinalIgnoreCase)))
            throw BlockedPlan(specification, "SQLite table scan");
        if (string.IsNullOrWhiteSpace(specification.IndexName))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' has no declared provider-native index and is blocked pending a storage redesign.");
        var search = lines.Where(line => line.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase) &&
                                        line.Contains("USING", StringComparison.OrdinalIgnoreCase) &&
                                        line.Contains("INDEX " + physicalIndexName, StringComparison.OrdinalIgnoreCase) &&
                                        (line.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) || line.Contains("SCAN", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (search.Length != 1 || !search[0].Contains(physicalIndexName, StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact provider-owned index search.");
    }

    private static void ValidatePostgreSqlPlan(string plan, DiagnosticsNativeRouteSpec specification, string physicalIndexName)
    {
        using var document = ParseJson(plan, "PostgreSQL");
        var nodes = FindObjects(document.RootElement, "Node Type").ToArray();
        if (nodes.Any(node => node.TryGetProperty("Node Type", out var kind) &&
                             kind.ValueKind == JsonValueKind.String &&
            (kind.GetString()?.Contains("Sort", StringComparison.OrdinalIgnoreCase) == true ||
                              string.Equals(kind.GetString(), "Materialize", StringComparison.OrdinalIgnoreCase))) ||
            FindObjects(document.RootElement, "Sort Method").Any() ||
            HasSpillMarker(document.RootElement))
            throw BlockedPlan(specification, "PostgreSQL sort or materialization spill");
        if (nodes.Any(node => node.TryGetProperty("Node Type", out var kind) &&
                             kind.ValueKind == JsonValueKind.String &&
                             kind.GetString()?.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase) == true))
            throw BlockedPlan(specification, "PostgreSQL sequential scan");
        var matches = nodes.Where(node => node.TryGetProperty("Node Type", out var kind) &&
                                          kind.ValueKind == JsonValueKind.String &&
                                          kind.GetString() is "Index Scan" or "Index Only Scan" &&
                                          node.TryGetProperty("Relation Name", out var relation) &&
                                          relation.ValueKind == JsonValueKind.String &&
                                          relation.GetString() == specification.TableName &&
                                          node.TryGetProperty("Index Name", out var index) &&
                                          index.ValueKind == JsonValueKind.String &&
                                          index.GetString() == physicalIndexName).ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact PostgreSQL index scan.");
    }

    private static void ValidatePostgreSqlBoundedScanSortPlan(
        string plan,
        DiagnosticsNativeRouteSpec specification)
    {
        using var document = ParseJson(plan, "PostgreSQL");
        var root = document.RootElement;
        var sort = default(JsonElement);
        var exactTopology = root.ValueKind == JsonValueKind.Array &&
                            root.GetArrayLength() == 1 &&
                            root[0].TryGetProperty("Plan", out var limit) &&
                            PostgreSqlNodeIs(limit, "Limit") &&
                            TryGetSinglePostgreSqlPlanChild(limit, out sort) &&
                            PostgreSqlNodeIs(sort, "Sort") &&
                            TryGetSinglePostgreSqlPlanChild(sort, out var scan) &&
                            PostgreSqlNodeIs(scan, "Seq Scan") &&
                            scan.TryGetProperty("Relation Name", out var relation) &&
                            relation.ValueKind == JsonValueKind.String &&
                            string.Equals(relation.GetString(), specification.TableName, StringComparison.Ordinal) &&
                            PostgreSqlOrdinalSubplansMatch(scan, specification);
        var exactOrdering = exactTopology && PostgreSqlOrderingMatches(sort, specification);
        var spilledSort = FindObjects(document.RootElement, "Sort Method").Any(node =>
                              node.GetProperty("Sort Method").ValueKind == JsonValueKind.String &&
                              node.GetProperty("Sort Method").GetString()?.Contains("external", StringComparison.OrdinalIgnoreCase) == true) ||
                          FindObjects(document.RootElement, "Sort Space Type").Any(node =>
                              node.GetProperty("Sort Space Type").ValueKind == JsonValueKind.String &&
                              string.Equals(node.GetProperty("Sort Space Type").GetString(), "Disk", StringComparison.OrdinalIgnoreCase));
        if (!exactTopology || !exactOrdering || spilledSort ||
            HasSpillMarker(document.RootElement))
            throw BlockedPlan(
                specification,
                "PostgreSQL bounded catalog plan is not exactly one sequential scan, one in-memory sort, and one limit");
    }

    private static bool PostgreSqlNodeIs(JsonElement node, string kind) =>
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty("Node Type", out var nodeType) &&
        nodeType.ValueKind == JsonValueKind.String &&
        string.Equals(nodeType.GetString(), kind, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSinglePostgreSqlPlanChild(JsonElement node, out JsonElement child)
    {
        child = default;
        if (!node.TryGetProperty("Plans", out var plans) ||
            plans.ValueKind != JsonValueKind.Array ||
            plans.GetArrayLength() != 1)
            return false;
        child = plans[0];
        return true;
    }

    private static bool PostgreSqlOrdinalSubplansMatch(
        JsonElement scan,
        DiagnosticsNativeRouteSpec specification)
    {
        var expectedColumns = specification.EffectiveOrdering
            .Where(term => IsOrdinalStringOrderColumn(term.Column))
            .Select(term => term.Column)
            .ToArray();
        var expected = expectedColumns.Length;
        if (!scan.TryGetProperty("Plans", out var plans))
            return expected == 0;
        if (plans.ValueKind != JsonValueKind.Array || plans.GetArrayLength() != expected)
            return false;
        for (var index = 0; index < expected; index++)
        {
            var aggregate = plans[index];
            var expectedAlias = index == 0 ? "chars" : $"chars_{index}";
            if (!PostgreSqlNodeIs(aggregate, "Aggregate") ||
                !aggregate.TryGetProperty("Parent Relationship", out var relationship) ||
                relationship.ValueKind != JsonValueKind.String ||
                !string.Equals(relationship.GetString(), "SubPlan", StringComparison.Ordinal) ||
                !aggregate.TryGetProperty("Subplan Name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), $"SubPlan {index + 1}", StringComparison.Ordinal) ||
                !PostgreSqlAggregateOutputMatches(aggregate, expectedAlias) ||
                !TryGetSinglePostgreSqlPlanChild(aggregate, out var function) ||
                !PostgreSqlNodeIs(function, "Function Scan") ||
                function.TryGetProperty("Plans", out _) ||
                !function.TryGetProperty("Function Name", out var functionName) ||
                functionName.ValueKind != JsonValueKind.String ||
                !string.Equals(functionName.GetString(), "unnest", StringComparison.Ordinal) ||
                !PostgreSqlFunctionCallMatches(
                    function,
                    specification.TableName,
                    expectedColumns[index],
                    expectedAlias))
                return false;
        }
        return true;
    }

    private static bool PostgreSqlAggregateOutputMatches(JsonElement aggregate, string alias)
    {
        if (!aggregate.TryGetProperty("Output", out var output) ||
            output.ValueKind != JsonValueKind.Array ||
            output.GetArrayLength() != 1 ||
            output[0].ValueKind != JsonValueKind.String)
            return false;

        var expected =
            $"string_agg(CASE WHEN (ascii({alias}.ch) <= 65535) THEN lpad(to_hex(ascii({alias}.ch)), 4, '0'::text) ELSE " +
            $"(lpad(to_hex((55296 + ((ascii({alias}.ch) - 65536) >> 10))), 4, '0'::text) || " +
            $"lpad(to_hex((56320 + ((ascii({alias}.ch) - 65536) & 1023))), 4, '0'::text)) END, ''::text ORDER BY {alias}.ord)";
        return string.Equals(
            CanonicalPostgreSqlVerboseExpression(output[0].GetString()!),
            CanonicalPostgreSqlVerboseExpression(expected),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PostgreSqlFunctionCallMatches(
        JsonElement function,
        string table,
        string column,
        string alias)
    {
        if (!function.TryGetProperty("Alias", out var actualAlias) ||
            actualAlias.ValueKind != JsonValueKind.String ||
            !string.Equals(actualAlias.GetString(), alias, StringComparison.Ordinal) ||
            !function.TryGetProperty("Output", out var output) ||
            output.ValueKind != JsonValueKind.Array ||
            output.GetArrayLength() != 2 ||
            output[0].ValueKind != JsonValueKind.String ||
            output[1].ValueKind != JsonValueKind.String ||
            !string.Equals(output[0].GetString(), $"{alias}.ch", StringComparison.Ordinal) ||
            !string.Equals(output[1].GetString(), $"{alias}.ord", StringComparison.Ordinal) ||
            !function.TryGetProperty("Function Call", out var call) ||
            call.ValueKind != JsonValueKind.String)
            return false;

        var expected = $"unnest(string_to_array(({table}.{column})::text, NULL::text))";
        return string.Equals(
            CanonicalPostgreSqlVerboseExpression(call.GetString()!),
            CanonicalPostgreSqlVerboseExpression(expected),
            StringComparison.Ordinal);
    }

    private static string CanonicalPostgreSqlVerboseExpression(string value) =>
        Regex.Replace(value, "[\\s\\\"]", string.Empty, RegexOptions.CultureInvariant);

    private static bool PostgreSqlOrderingMatches(
        JsonElement sort,
        DiagnosticsNativeRouteSpec specification)
    {
        if (!sort.TryGetProperty("Sort Key", out var sortKey) || sortKey.ValueKind != JsonValueKind.Array)
            return false;
        var terms = sortKey.EnumerateArray().ToArray();
        if (terms.Any(key => key.ValueKind != JsonValueKind.String))
            return false;
        var subplan = 0;
        var termIndex = 0;
        foreach (var expected in specification.EffectiveOrdering)
        {
            if (specification.RequiresNullRank(expected.Column))
            {
                if (termIndex >= terms.Length ||
                    !PostgreSqlNullRankMatches(terms[termIndex].GetString()!, expected.Column))
                    return false;
                termIndex++;
            }
            if (termIndex >= terms.Length)
                return false;
            if (IsOrdinalStringOrderColumn(expected.Column))
            {
                subplan++;
                if (!PostgreSqlSubplanOrderMatches(terms[termIndex].GetString()!, subplan, expected.Direction))
                    return false;
            }
            else if (!PostgreSqlColumnOrderMatches(
                         terms[termIndex].GetString()!,
                         expected.Column,
                         expected.Direction))
                return false;
            termIndex++;
        }
        return termIndex == terms.Length;
    }

    private static bool PostgreSqlNullRankMatches(string value, string column)
    {
        var canonical = CanonicalPostgreSqlSortKey(value);
        return Regex.IsMatch(
            canonical,
            $@"^CASEWHEN(?:[A-Za-z_][A-Za-z0-9_]*\.)?{Regex.Escape(column)}ISNULLTHEN1ELSE0END(?:ASC)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool PostgreSqlColumnOrderMatches(
        string value,
        string column,
        RuntimeNativeOrderDirection direction)
    {
        var canonical = CanonicalPostgreSqlSortKey(value);
        var match = Regex.Match(
            canonical,
            $@"^(?:[A-Za-z_][A-Za-z0-9_]*\.)?{Regex.Escape(column)}(?<direction>ASC|DESC)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && ParseOrderDirection(match.Groups["direction"].Value) == direction;
    }

    private static bool PostgreSqlSubplanOrderMatches(
        string value,
        int subplan,
        RuntimeNativeOrderDirection direction)
    {
        var match = Regex.Match(
            CanonicalPostgreSqlSortKey(value),
            $@"^COALESCESubPlan{subplan},''(?<direction>ASC|DESC)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && ParseOrderDirection(match.Groups["direction"].Value) == direction;
    }

    private static string CanonicalPostgreSqlSortKey(string value)
    {
        var normalized = NormalizeSqlCommand(value);
        normalized = Regex.Replace(
            normalized,
            @"::[A-Za-z_][A-Za-z0-9_]*(?:\[\])?",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(normalized, @"[()\s]", string.Empty, RegexOptions.CultureInvariant);
    }

    private static RuntimeNativeOrderDirection ParseOrderDirection(string value) =>
        string.Equals(value, "DESC", StringComparison.OrdinalIgnoreCase)
            ? RuntimeNativeOrderDirection.Descending
            : RuntimeNativeOrderDirection.Ascending;

    private static bool IsOrdinalStringOrderColumn(string column) =>
        column is "id" or "idOrderKey" or "traceKey" or "spanId";

    private static void ValidateSqlServerPlan(string plan, DiagnosticsNativeRouteSpec specification, string physicalIndexName)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(plan, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var relops = document.Descendants().Where(element => element.Name.LocalName == "RelOp").ToArray();
            if (relops.Any(IsSqlServerSortOrMaterialization) ||
                document.Descendants().Any(element => element.Name.LocalName.Contains("Sort", StringComparison.OrdinalIgnoreCase) ||
                                                      element.Name.LocalName.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                                                      element.Name.LocalName.Contains("Material", StringComparison.OrdinalIgnoreCase)) ||
                document.Descendants().Any(element => element.Name.LocalName is "SpillOccurred" or "SpillWarning" or "SpillToTempDb") ||
                document.Descendants().SelectMany(element => element.Attributes()).Any(attribute =>
                    attribute.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) && IsPositiveFlag(attribute.Value)))
                throw BlockedPlan(specification, "SQL Server sort or materialization spill");
            if (relops.Any(element => element.Attribute("PhysicalOp")?.Value.Contains("Scan", StringComparison.OrdinalIgnoreCase) == true))
                throw BlockedPlan(specification, "SQL Server scan");
            var matches = relops.Where(element => element.Attribute("PhysicalOp")?.Value == "Index Seek" &&
                element.Descendants().Any(objectElement => objectElement.Name.LocalName == "Object" &&
                    objectElement.Attribute("Table")?.Value.Trim('[', ']') == specification.TableName &&
                    objectElement.Attribute("Index")?.Value.Trim('[', ']') == physicalIndexName)).ToArray();
            if (matches.Length != 1)
                throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact SQL Server index seek.");
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PerformanceContractException($"Diagnostics SQL Server native plan is invalid: {exception.Message}");
        }
    }

    private static void ValidateSqlServerBoundedScanSortPlan(
        string plan,
        DiagnosticsNativeRouteSpec specification)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(plan, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var relops = document.Descendants().Where(element => element.Name.LocalName == "RelOp").ToArray();
            var allScans = relops.Where(element =>
                element.Attribute("PhysicalOp")?.Value.Contains("Scan", StringComparison.OrdinalIgnoreCase) == true).ToArray();
            var scans = allScans.Where(element =>
                element.Descendants().Any(objectElement =>
                    objectElement.Name.LocalName == "Object" &&
                    objectElement.Attribute("Table")?.Value.Trim('[', ']') == specification.TableName)).ToArray();
            var sorts = relops.Where(element =>
                element.Attribute("PhysicalOp")?.Value.Contains("Sort", StringComparison.OrdinalIgnoreCase) == true).ToArray();
            var tops = relops.Where(element =>
                element.Attribute("PhysicalOp")?.Value.Contains("Top", StringComparison.OrdinalIgnoreCase) == true).ToArray();
            // ShowPlan rewrites the renderer's null-rank and DATALENGTH order expressions into
            // Compute Scalar Expr references. Validate their exact physical order and directions
            // from the retained definitions; this artifact must prove one Top over one Sort over one scan.
            var exactTopology = tops.Length == 1 &&
                                sorts.Length == 1 &&
                                scans.Length == 1 &&
                                sorts[0].Ancestors().Contains(tops[0]) &&
                                scans[0].Ancestors().Contains(sorts[0]);
            var exactOrdering = exactTopology &&
                                SqlServerOrderingMatches(document, sorts[0], specification);
            var unexpected = relops.Any(element =>
            {
                var operation = element.Attribute("PhysicalOp")?.Value ?? string.Empty;
                return operation is not ("Top" or "Filter" or "Sort" or "Compute Scalar" or "Table Scan") ||
                       operation.Contains("Seek", StringComparison.OrdinalIgnoreCase) ||
                       operation.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                       operation.Contains("Material", StringComparison.OrdinalIgnoreCase);
            });
            var spilled = document.Descendants().Any(element =>
                              element.Name.LocalName is "SpillOccurred" or "SpillWarning" or "SpillToTempDb") ||
                          document.Descendants().SelectMany(element => element.Attributes()).Any(attribute =>
                              attribute.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) &&
                              IsPositiveFlag(attribute.Value));
            if (allScans.Length != 1 || scans.Length != 1 || sorts.Length != 1 || tops.Length != 1 ||
                !exactTopology || !exactOrdering || unexpected || spilled)
                throw BlockedPlan(
                    specification,
                    "SQL Server bounded catalog plan is not exactly one scan, one in-memory sort, and its complete physical ordering");
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PerformanceContractException($"Diagnostics SQL Server native plan is invalid: {exception.Message}");
        }
    }

    private enum SqlServerSortKeyKind
    {
        NullRank,
        Value,
        ByteLength
    }

    private const string SqlServerOrdinalCollation = "Latin1_General_100_BIN2";

    private static bool SqlServerOrderingMatches(
        System.Xml.Linq.XDocument document,
        System.Xml.Linq.XElement sort,
        DiagnosticsNativeRouteSpec specification)
    {
        var orderBy = sort.Descendants().Where(element => element.Name.LocalName == "OrderBy").ToArray();
        if (orderBy.Length != 1)
            return false;

        var orderByElements = orderBy[0].Elements().ToArray();
        if (orderByElements.Any(element => element.Name.LocalName != "OrderByColumn"))
            return false;
        var columns = orderByElements;
        var expected = new List<(SqlServerSortKeyKind Kind, string Column, RuntimeNativeOrderDirection Direction)>();
        foreach (var term in specification.EffectiveOrdering)
        {
            if (specification.RequiresNullRank(term.Column))
                expected.Add((SqlServerSortKeyKind.NullRank, term.Column, RuntimeNativeOrderDirection.Ascending));
            expected.Add((SqlServerSortKeyKind.Value, term.Column, term.Direction));
            if (IsOrdinalStringOrderColumn(term.Column))
                expected.Add((SqlServerSortKeyKind.ByteLength, term.Column, term.Direction));
        }

        if (columns.Length != expected.Count)
            return false;

        foreach (var (column, index) in columns.Select((column, index) => (column, index)))
        {
            var ascending = column.Attribute("Ascending")?.Value;
            var direction = ascending switch
            {
                "1" => RuntimeNativeOrderDirection.Ascending,
                "0" => RuntimeNativeOrderDirection.Descending,
                _ => (RuntimeNativeOrderDirection?)null
            };
            if (direction is null ||
                !TryResolveSqlServerSortKey(document, column, specification, out var key) ||
                !string.Equals(key.Column, expected[index].Column, StringComparison.OrdinalIgnoreCase) ||
                key.Kind != expected[index].Kind ||
                direction.Value != expected[index].Direction)
                return false;
        }

        return true;
    }

    private static bool TryResolveSqlServerSortKey(
        System.Xml.Linq.XDocument document,
        System.Xml.Linq.XElement orderByColumn,
        DiagnosticsNativeRouteSpec specification,
        out (SqlServerSortKeyKind Kind, string Column) key)
    {
        key = default;
        var references = orderByColumn.Descendants()
            .Where(element => element.Name.LocalName == "ColumnReference")
            .ToArray();
        if (references.Length != 1)
            return false;

        var reference = references[0].Attribute("Column")?.Value.Trim('[', ']');
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        if (!reference.StartsWith("Expr", StringComparison.OrdinalIgnoreCase))
        {
            if (!specification.EffectiveOrdering.Any(term =>
                    string.Equals(term.Column, reference, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (IsOrdinalStringOrderColumn(reference))
                return false;
            key = (SqlServerSortKeyKind.Value, reference);
            return true;
        }

        var definitions = document.Descendants()
            .Where(element => element.Name.LocalName == "DefinedValue" &&
                              element.Descendants().Any(child =>
                                  child.Name.LocalName == "ColumnReference" &&
                                  string.Equals(child.Attribute("Column")?.Value.Trim('[', ']'), reference, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var expressions = definitions
            .SelectMany(definition => definition.Elements()
                .Where(element => element.Name.LocalName == "ScalarOperator")
                .Select(element => element.Attribute("ScalarString")?.Value))
            .Where(expression => !string.IsNullOrWhiteSpace(expression))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (expressions.Length != 1)
            return false;

        var expression = expressions[0]!;
        if (TryParseSqlServerNullRankExpression(expression, specification, out var sourceColumn))
        {
            key = (SqlServerSortKeyKind.NullRank, sourceColumn);
            return true;
        }
        if (TryParseSqlServerByteLengthExpression(expression, specification, out sourceColumn))
        {
            key = (SqlServerSortKeyKind.ByteLength, sourceColumn);
            return true;
        }
        if (TryParseSqlServerValueExpression(expression, specification, requireOrdinalCollation: true, out sourceColumn))
        {
            key = (SqlServerSortKeyKind.Value, sourceColumn);
            return true;
        }
        return false;
    }

    private static bool TryParseSqlServerNullRankExpression(
        string expression,
        DiagnosticsNativeRouteSpec specification,
        out string column)
    {
        column = string.Empty;
        var match = Regex.Match(
            expression,
            @"^CASE\s+WHEN\s+(?<value>(?:(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\.)*(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)(?:\s+COLLATE\s+[A-Za-z_][A-Za-z0-9_]*)?)\s+IS\s+NULL\s+THEN\s+\(?1\)?\s+ELSE\s+\(?0\)?\s+END$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success &&
               TryParseSqlServerValueExpression(
                   match.Groups["value"].Value,
                   specification,
                   requireOrdinalCollation: false,
                   out column);
    }

    private static bool TryParseSqlServerByteLengthExpression(
        string expression,
        DiagnosticsNativeRouteSpec specification,
        out string column)
    {
        column = string.Empty;
        var match = Regex.Match(
            expression,
            @"^DATALENGTH\(\s*(?<value>(?:(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\.)*(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)(?:\s+COLLATE\s+[A-Za-z_][A-Za-z0-9_]*)?)\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success &&
               TryParseSqlServerValueExpression(
                   match.Groups["value"].Value,
                   specification,
                   requireOrdinalCollation: true,
                   out column) &&
               IsOrdinalStringOrderColumn(column);
    }

    private static bool TryParseSqlServerValueExpression(
        string expression,
        DiagnosticsNativeRouteSpec specification,
        bool requireOrdinalCollation,
        out string column)
    {
        var match = Regex.Match(
            expression,
            @"^(?:(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\.)*(?<column>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)(?:\s+COLLATE\s+(?<collation>[A-Za-z_][A-Za-z0-9_]*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var parsedColumn = match.Success ? match.Groups["column"].Value.Trim('[', ']') : string.Empty;
        column = parsedColumn;
        if (!match.Success || !specification.EffectiveOrdering.Any(term =>
                string.Equals(term.Column, parsedColumn, StringComparison.OrdinalIgnoreCase)))
            return false;

        var collation = match.Groups["collation"].Value;
        var ordinal = IsOrdinalStringOrderColumn(column);
        return !ordinal
            ? string.IsNullOrEmpty(collation)
            : string.IsNullOrEmpty(collation)
                ? !requireOrdinalCollation
                : string.Equals(collation, SqlServerOrdinalCollation, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> ValidateMongoCommand(
        string command,
        DiagnosticsNativeRouteSpec specification,
        string rawPlan)
    {
        // The observer's descriptive text is only "MongoDB.Aggregate(page)". Reparse the retained
        // server explain response and require the envelope command to be byte-structurally the same
        // native command, so neither a logical collection nor a synthetic scope field can be claimed.
        MongoExplainCommandInspector.RequireCommandMatchesExplain(command, rawPlan);
        var actual = MongoExplainCommandInspector.ParseCommandText(command);
        var isAggregate = actual.TryGetProperty("aggregate", out var aggregate) &&
                          aggregate.ValueKind == JsonValueKind.String;
        var isFind = actual.TryGetProperty("find", out var find) &&
                     find.ValueKind == JsonValueKind.String;
        if (isAggregate == isFind)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB command must be exactly one aggregate or find command.");

        var collection = (isAggregate ? aggregate : find).GetString();
        ValidateMongoPhysicalCollection(collection, specification);

        return isAggregate
            ? ValidateMongoAggregateCommand(actual, specification)
            : ValidateMongoFindCommand(actual, specification);
    }

    private static void ValidateMongoPhysicalCollection(string? collection, DiagnosticsNativeRouteSpec specification)
        => ValidateMongoPhysicalCollection(collection, specification.RouteIdentity, specification.TableName);

    private static void ValidateMongoPhysicalCollection(string? collection, DiagnosticsTraceDetailConstituentSpec specification)
        => ValidateMongoPhysicalCollection(collection, specification.RouteIdentity, specification.TableName);

    private static void ValidateMongoPhysicalCollection(string? collection, string routeIdentity, string tableName)
    {
        var prefix = tableName + "__scope__";
        if (string.IsNullOrWhiteSpace(collection) ||
            string.Equals(collection, tableName, StringComparison.Ordinal) ||
            !collection.StartsWith(prefix, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Diagnostics route '{routeIdentity}' MongoDB command must bind its scoped physical collection, not logical collection '{tableName}'.");

        var suffix = collection[prefix.Length..];
        if (suffix.Length != 64 || suffix.Any(character =>
                !((character is >= '0' and <= '9') || (character is >= 'A' and <= 'F'))))
            throw new PerformanceContractException(
                $"Diagnostics route '{routeIdentity}' MongoDB command does not bind the exact scoped physical collection name.");
    }

    private static IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> ValidateMongoAggregateCommand(
        JsonElement command,
        DiagnosticsNativeRouteSpec specification)
    {
        if (!command.TryGetProperty("pipeline", out var pipeline) || pipeline.ValueKind != JsonValueKind.Array ||
            !command.TryGetProperty("cursor", out var cursor) || cursor.ValueKind != JsonValueKind.Object)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command must retain its pipeline and cursor shape.");
        if (ContainsMongoProperty(pipeline, "__groundwork_scope"))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB command must not fabricate a __groundwork_scope predicate.");

        var stages = pipeline.EnumerateArray().ToArray();
        if (stages.Any(stage => stage.ValueKind != JsonValueKind.Object || stage.EnumerateObject().Count() != 1))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command contains a non-object pipeline stage.");
        var matchStages = stages.Select((stage, index) => (Stage: stage, Index: index))
            .Where(item => item.Stage.TryGetProperty("$match", out var match) && match.ValueKind == JsonValueKind.Object)
            .ToArray();
        if (matchStages.Length == 0 || matchStages[0].Index != 0 ||
            !ValidateMongoRoutePredicate(matchStages[0].Stage.GetProperty("$match"), specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command must retain only its exact route predicate.");

        var continuationStages = matchStages.Skip(1).Select(item => item.Stage.GetProperty("$match")).ToArray();
        if (continuationStages.Length != 0 &&
            (!specification.RouteIdentity.StartsWith("trace-detail/", StringComparison.Ordinal) ||
             continuationStages.Length != 1 ||
             !continuationStages[0].TryGetProperty("$or", out var keyset) ||
             !ValidateMongoContinuationFilter(keyset, specification)))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command contains an invalid continuation predicate.");

        var sortStages = stages.Select((stage, index) => (Stage: stage, Index: index))
            .Where(item => item.Stage.TryGetProperty("$sort", out var sort) && sort.ValueKind == JsonValueKind.Object)
            .ToArray();
        var limits = stages.Select((stage, index) => (Stage: stage, Index: index))
            .Where(item => item.Stage.TryGetProperty("$limit", out _)).ToArray();
        if (sortStages.Length != 1 || limits.Length != 1 ||
            matchStages.Any(item => item.Index >= sortStages[0].Index) ||
            sortStages[0].Index + 1 != limits[0].Index ||
            limits[0].Index != stages.Length - 1 ||
            stages.Take(sortStages[0].Index).Any(stage =>
                !stage.TryGetProperty("$match", out _) && !stage.TryGetProperty("$set", out _)) ||
            !ValidateMongoPipelineOrdering(pipeline, sortStages[0].Stage.GetProperty("$sort"), specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command does not bind its complete effective ordering.");

        if (!limits[0].Stage.GetProperty("$limit").TryGetInt32(out var finiteLimit) ||
            finiteLimit != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command does not bind its finite page limit.");

        return ParseMongoOrdering(sortStages[0].Stage.GetProperty("$sort"));
    }

    private static IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> ValidateMongoFindCommand(
        JsonElement command,
        DiagnosticsNativeRouteSpec specification)
    {
        if (!command.TryGetProperty("filter", out var filter) || filter.ValueKind != JsonValueKind.Object ||
            !ValidateMongoRoutePredicate(filter, specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB find command must retain only its exact route predicate.");
        if (filter.TryGetProperty("$or", out var keyset))
        {
            if (!specification.RouteIdentity.StartsWith("trace-detail/", StringComparison.Ordinal) ||
                !ValidateMongoContinuationFilter(keyset, specification))
                throw new PerformanceContractException(
                    $"Diagnostics route '{specification.RouteIdentity}' MongoDB find command contains an invalid continuation predicate.");
        }

        if (!command.TryGetProperty("sort", out var sort) || sort.ValueKind != JsonValueKind.Object ||
            !ValidateMongoOrdering(sort, specification) ||
            !command.TryGetProperty("limit", out var limit) || !limit.TryGetInt32(out var finiteLimit) ||
            finiteLimit != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB find command does not bind its complete ordering and finite page limit.");

        return ParseMongoOrdering(sort);
    }

    private static bool ValidateMongoRoutePredicate(JsonElement filter, DiagnosticsNativeRouteSpec specification)
    {
        if (filter.ValueKind != JsonValueKind.Object)
            return false;
        if (string.Equals(specification.RouteIdentity, "structured-log-replay", StringComparison.Ordinal))
            return ValidateMongoReplayRangePredicate(filter);
        var properties = filter.EnumerateObject().Where(property => property.Name != "$or").ToArray();
        var expected = specification.PredicateColumn is null ? Array.Empty<string>() : [specification.PredicateColumn];
        return properties.Length == expected.Length &&
               properties.Select(property => property.Name).Order(StringComparer.Ordinal)
                   .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
               properties.All(property => IsMongoEqualityValue(property.Value));
    }

    private static bool ValidateMongoReplayRangePredicate(JsonElement filter)
    {
        var properties = filter.EnumerateObject().ToArray();
        if (properties.Length != 1 || !string.Equals(properties[0].Name, "$and", StringComparison.Ordinal) ||
            properties[0].Value.ValueKind != JsonValueKind.Array)
            return false;
        var terms = properties[0].Value.EnumerateArray().ToArray();
        if (terms.Length != 1 || terms[0].ValueKind != JsonValueKind.Object)
            return false;
        var rangeProperties = terms[0].EnumerateObject().ToArray();
        if (rangeProperties.Length != 1 || !string.Equals(rangeProperties[0].Name, "sequence", StringComparison.Ordinal) ||
            rangeProperties[0].Value.ValueKind != JsonValueKind.Object)
            return false;
        var operators = rangeProperties[0].Value.EnumerateObject().ToArray();
        return operators.Length == 2 &&
               operators.Any(item => item.Name == "$gt" && IsMongoScalar(item.Value)) &&
               operators.Any(item => item.Name == "$lte" && IsMongoScalar(item.Value));
    }

    private static bool IsMongoEqualityValue(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
        value.TryGetProperty("$eq", out var equality) &&
        equality.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array;

    private static bool ValidateMongoContinuationFilter(JsonElement keyset, DiagnosticsNativeRouteSpec specification)
    {
        if (keyset.ValueKind != JsonValueKind.Array)
            return false;
        var branches = keyset.EnumerateArray().ToArray();
        var ordering = specification.EffectiveOrdering;
        if (branches.Length != ordering.Count)
            return false;
        for (var index = 0; index < branches.Length; index++)
        {
            var terms = index == 0
                ? [branches[index]]
                : branches[index].TryGetProperty("$and", out var conjunction) && conjunction.ValueKind == JsonValueKind.Array
                    ? conjunction.EnumerateArray().ToArray()
                    : [];
            if (terms.Length != index + 1)
                return false;
            for (var termIndex = 0; termIndex < terms.Length; termIndex++)
            {
                var term = ordering[termIndex];
                if (termIndex < index
                    ? !ValidateMongoContinuationEquality(terms[termIndex], term)
                    : !ValidateMongoContinuationAfter(terms[termIndex], term))
                    return false;
            }
        }
        return true;
    }

    private static bool ValidateMongoContinuationEquality(JsonElement value, RuntimeNativeOrderTerm term)
    {
        if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
            value.TryGetProperty(term.Column, out var equality))
            return IsMongoEqualityValue(equality);
        return value.ValueKind == JsonValueKind.Object &&
               ContainsMongoOperator(value, "$eq") && ContainsMongoString(value, "$" + term.Column);
    }

    private static bool ValidateMongoContinuationAfter(JsonElement value, RuntimeNativeOrderTerm term)
    {
        var operatorName = term.Direction == RuntimeNativeOrderDirection.Ascending ? "$gt" : "$lt";
        if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
            value.TryGetProperty(term.Column, out var strict))
            return strict.ValueKind == JsonValueKind.Object && strict.EnumerateObject().Count() == 1 &&
                   strict.TryGetProperty(operatorName, out var operand) && IsMongoScalar(operand);

        // A non-null Mongo cursor uses an $expr ordinal comparison for string columns and wraps
        // the strict term with a null branch when the portable null order is Last.
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$or", out var alternatives) &&
            alternatives.ValueKind == JsonValueKind.Array)
        {
            var branches = alternatives.EnumerateArray().ToArray();
            return branches.Length == 2 &&
                   branches.Count(branch => ValidateMongoStrictAfter(branch, term, operatorName)) == 1 &&
                   branches.Count(branch => ValidateMongoNullAfter(branch, term)) == 1;
        }
        return ValidateMongoStrictAfter(value, term, operatorName);
    }

    private static bool ValidateMongoStrictAfter(JsonElement value, RuntimeNativeOrderTerm term, string operatorName)
    {
        if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
            value.TryGetProperty(term.Column, out var strict))
            return strict.ValueKind == JsonValueKind.Object && strict.EnumerateObject().Count() == 1 &&
                   strict.TryGetProperty(operatorName, out var operand) && IsMongoScalar(operand);
        return value.ValueKind == JsonValueKind.Object &&
               ContainsMongoOperator(value, operatorName) &&
               ContainsMongoOperator(value, "$ne") &&
               ContainsMongoString(value, "$" + term.Column);
    }

    private static bool ValidateMongoNullAfter(JsonElement value, RuntimeNativeOrderTerm term) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
        value.TryGetProperty(term.Column, out var nullValue) && nullValue.ValueKind == JsonValueKind.Null;

    private static bool ValidateMongoPipelineOrdering(
        JsonElement pipeline,
        JsonElement sort,
        DiagnosticsNativeRouteSpec specification)
    {
        if (!specification.EffectiveOrdering.Any(term => IsMongoStringOrderColumn(term.Column)) &&
            ValidateMongoOrdering(sort, specification))
            return true;

        var actual = ParseMongoOrdering(sort);
        // Index metadata can prove every ordered column non-null independently of whether the
        // provider options recognize a physical string order key. Preview.11's StorageUnit helper
        // carries the former but not the latter for Elsa's explicit idOrderKey column, so the
        // released Mongo shape suppresses null ranks while still rendering ordinal helpers for
        // both idOrderKey and id. Keep the candidates explicit and validate every helper below.
        var expected = new[]
            {
                ExpectedMongoRenderedOrdering(specification, includeNullRanks: true),
                ExpectedMongoRenderedOrdering(specification, includeNullRanks: false)
            }
            .FirstOrDefault(candidate => OrderingMatches(actual, candidate));
        if (expected is null)
            return false;

        var setFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var stage in pipeline.EnumerateArray())
        {
            if (stage.ValueKind != JsonValueKind.Object)
                continue;
            if (stage.TryGetProperty("$sort", out _))
                break;
            if (!stage.TryGetProperty("$set", out var set) || set.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var property in set.EnumerateObject())
            {
                if (!setFields.TryAdd(property.Name, property.Value))
                    return false;
            }
        }
        var helpers = expected
            .Where(term => term.Column.StartsWith("_groundwork_", StringComparison.Ordinal))
            .Select(term => term.Column)
            .ToHashSet(StringComparer.Ordinal);
        return setFields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(helpers) &&
               helpers.All(helper => setFields.TryGetValue(helper, out var value) &&
                                     ValidateMongoOrderingHelper(helper, value, specification));
    }

    private const string MongoOrdinalKeyFunctionBody =
        "function(value) { if (value === null || value === undefined) return null; var key = ''; for (var i = 0; i < value.length; i++) { var unit = value.charCodeAt(i).toString(16); key += ('0000' + unit).slice(-4); } return key; }";

    private static bool ValidateMongoOrderingHelper(
        string helperName,
        JsonElement value,
        DiagnosticsNativeRouteSpec specification)
    {
        const string nullRankPrefix = "_groundwork_null_rank_";
        const string ordinalKeyPrefix = "_groundwork_ordinal_key_";
        var prefix = helperName.StartsWith(nullRankPrefix, StringComparison.Ordinal)
            ? nullRankPrefix
            : helperName.StartsWith(ordinalKeyPrefix, StringComparison.Ordinal)
                ? ordinalKeyPrefix
                : null;
        if (prefix is null ||
            !int.TryParse(helperName[prefix.Length..], out var index) ||
            index < 0 || index >= specification.EffectiveOrdering.Count)
            return false;

        var source = "$" + specification.EffectiveOrdering[index].Column;
        return prefix == nullRankPrefix
            ? ValidateMongoNullRankHelper(value, source)
            : ValidateMongoOrdinalKeyHelper(value, source);
    }

    private static bool ValidateMongoNullRankHelper(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 ||
            !value.TryGetProperty("$cond", out var condition) || condition.ValueKind != JsonValueKind.Array)
            return false;
        var terms = condition.EnumerateArray().ToArray();
        if (terms.Length != 3 || terms[0].ValueKind != JsonValueKind.Object ||
            terms[0].EnumerateObject().Count() != 1 ||
            !terms[0].TryGetProperty("$eq", out var equality) || equality.ValueKind != JsonValueKind.Array)
            return false;
        var operands = equality.EnumerateArray().ToArray();
        return operands.Length == 2 &&
               operands[0].ValueKind == JsonValueKind.String && operands[0].GetString() == source &&
               operands[1].ValueKind == JsonValueKind.Null &&
               terms[1].TryGetInt32(out var firstRank) &&
               terms[2].TryGetInt32(out var secondRank) &&
               firstRank is 0 or 1 && secondRank is 0 or 1 && firstRank != secondRank;
    }

    private static bool ValidateMongoOrdinalKeyHelper(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 ||
            !value.TryGetProperty("$function", out var function) || function.ValueKind != JsonValueKind.Object ||
            function.EnumerateObject().Count() != 3 ||
            !function.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String ||
            body.GetString() != MongoOrdinalKeyFunctionBody ||
            !function.TryGetProperty("lang", out var language) || language.ValueKind != JsonValueKind.String ||
            language.GetString() != "js" ||
            !function.TryGetProperty("args", out var arguments) || arguments.ValueKind != JsonValueKind.Array)
            return false;
        var values = arguments.EnumerateArray().ToArray();
        return values.Length == 1 && values[0].ValueKind == JsonValueKind.String && values[0].GetString() == source;
    }

    private static IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> ExpectedMongoRenderedOrdering(
        DiagnosticsNativeRouteSpec specification,
        bool includeNullRanks)
    {
        var expected = new List<(string Column, RuntimeNativeOrderDirection Direction)>();
        foreach (var (term, index) in specification.EffectiveOrdering.Select((term, index) => (term, index)))
        {
            if (includeNullRanks)
                expected.Add(("_groundwork_null_rank_" + index, RuntimeNativeOrderDirection.Ascending));
            expected.Add((IsMongoStringOrderColumn(term.Column)
                    ? "_groundwork_ordinal_key_" + index
                    : term.Column,
                term.Direction));
        }
        return expected;
    }

    private static bool OrderingMatches(
        IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> actual,
        IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair =>
            pair.First.Column == pair.Second.Column && pair.First.Direction == pair.Second.Direction);

    private static bool IsMongoStringOrderColumn(string column) =>
        column is "id" or "idOrderKey" or "serviceNameKey" or "traceKey" or "spanId";

    private static bool ValidateMongoOrdering(JsonElement sort, DiagnosticsNativeRouteSpec specification) =>
        ParseMongoOrdering(sort).Count == specification.EffectiveOrdering.Count &&
        ParseMongoOrdering(sort).Zip(specification.EffectiveOrdering).All(pair =>
            string.Equals(pair.First.Column, pair.Second.Column, StringComparison.Ordinal) &&
            pair.First.Direction == pair.Second.Direction);

    private static List<(string Column, RuntimeNativeOrderDirection Direction)> ParseMongoOrdering(JsonElement sort) =>
        sort.ValueKind != JsonValueKind.Object
            ? []
            : sort.EnumerateObject().Select(property =>
            {
                if (!property.Value.TryGetInt32(out var direction) || direction is not (-1 or 1))
                    return (Column: string.Empty, Direction: (RuntimeNativeOrderDirection)(-1));
                return (Column: property.Name, Direction: direction == -1
                    ? RuntimeNativeOrderDirection.Descending
                    : RuntimeNativeOrderDirection.Ascending);
            }).ToList();

    private static bool IsMongoScalar(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static bool ContainsMongoOperator(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty(name, out _))
                return true;
            return value.EnumerateObject().Any(property => ContainsMongoOperator(property.Value, name));
        }
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => ContainsMongoOperator(item, name));
    }

    private static bool ContainsMongoString(JsonElement value, string expected)
    {
        if (value.ValueKind == JsonValueKind.String)
            return string.Equals(value.GetString(), expected, StringComparison.Ordinal);
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().Any(property => ContainsMongoString(property.Value, expected));
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => ContainsMongoString(item, expected));
    }

    private static bool ContainsMongoProperty(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().Any(property => property.Name == name || ContainsMongoProperty(property.Value, name));
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => ContainsMongoProperty(item, name));
    }

    private static bool IsBoundedScanSortShape(
        string provider,
        string plan,
        DiagnosticsNativeRouteSpec specification)
    {
        try
        {
            switch (provider)
            {
                case "postgresql":
                    ValidatePostgreSqlBoundedScanSortPlan(plan, specification);
                    return true;
                case "sqlserver":
                    ValidateSqlServerBoundedScanSortPlan(plan, specification);
                    return true;
                case "mongodb":
                    return IsBoundedMongoScanSortShape(plan);
                default:
                    return false;
            }
        }
        catch (PerformanceContractException)
        {
            return false;
        }
    }

    private static bool IsBoundedMongoScanSortShape(string plan)
    {
        try
        {
            using var document = JsonDocument.Parse(plan);
            var winningPlans = FindPropertyValues(document.RootElement, "winningPlan").ToArray();
            if (winningPlans.Length != 1)
                return false;

            var stages = FindObjects(winningPlans[0], "stage").ToArray();
            var names = stages
                .Select(MongoStageName)
                .Where(name => name is not null)
                .ToArray();
            var limits = FindObjects(winningPlans[0], "limitAmount").ToArray();
            return names.Count(name => string.Equals(name, "COLLSCAN", StringComparison.OrdinalIgnoreCase)) == 1 &&
                   names.Count(name => string.Equals(name, "SORT", StringComparison.OrdinalIgnoreCase)) == 1 &&
                   !names.Any(name => string.Equals(name, "IXSCAN", StringComparison.OrdinalIgnoreCase)) &&
                   !names.Any(name => name!.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase)) &&
                   !HasSpillMarker(document.RootElement) &&
                   limits.Length != 0 &&
                   limits.All(limit => limit.GetProperty("limitAmount").TryGetInt32(out var value) && value == BoundedResourceLimit);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateMongoBoundedScanSortPlan(
        string plan,
        DiagnosticsNativeRouteSpec specification,
        IReadOnlyList<(string Column, RuntimeNativeOrderDirection Direction)> commandOrdering)
    {
        using var document = ParseJson(plan, "MongoDB");
        var winningPlans = FindPropertyValues(document.RootElement, "winningPlan").ToArray();
        var stages = winningPlans.Length == 1
            ? FindObjects(winningPlans[0], "stage").ToArray()
            : [];
        var allStages = FindObjects(document.RootElement, "stage").ToArray();
        var names = stages
            .Select(MongoStageName)
            .Where(name => name is not null)
            .ToArray();
        if (winningPlans.Length != 1 ||
            names.Count(name => string.Equals(name, "COLLSCAN", StringComparison.OrdinalIgnoreCase)) != 1 ||
            names.Count(name => string.Equals(name, "SORT", StringComparison.OrdinalIgnoreCase)) != 1)
            throw BlockedPlan(specification, "MongoDB bounded scan/sort plan is missing its explicit collection scan or sort");
        if (names.Any(name => string.Equals(name, "IXSCAN", StringComparison.OrdinalIgnoreCase)))
            throw BlockedPlan(specification, "MongoDB bounded scan/sort plan unexpectedly uses an index scan");
        if (allStages.Select(MongoStageName).Any(name => name?.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) == true) ||
            HasSpillMarker(document.RootElement))
            throw BlockedPlan(specification, "MongoDB bounded scan/sort plan has a sort or materialization spill");

        var sortStages = stages.Where(stage => string.Equals(MongoStageName(stage), "SORT", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sortStages.Length != 1 ||
            !sortStages[0].TryGetProperty("sortPattern", out var sortPattern) ||
            !OrderingMatches(ParseMongoOrdering(sortPattern), commandOrdering))
            throw BlockedPlan(specification, "MongoDB bounded scan/sort plan does not bind its complete effective ordering");

        var limits = FindObjects(winningPlans[0], "limitAmount").ToArray();
        if (limits.Length == 0 ||
            limits.Any(limit => !limit.GetProperty("limitAmount").TryGetInt32(out var value) || value != specification.FiniteLimit))
            throw BlockedPlan(specification, "MongoDB bounded scan/sort plan does not bind the frozen finite page limit");
    }

    private static string? MongoStageName(JsonElement stage) =>
        stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void ValidateMongoPlan(string plan, DiagnosticsNativeRouteSpec specification, string physicalIndexName)
    {
        using var document = ParseJson(plan, "MongoDB");
        var stages = FindObjects(document.RootElement, "stage").ToArray();
        if (stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String &&
                                (value.GetString()?.Contains("SORT", StringComparison.OrdinalIgnoreCase) == true ||
                                 value.GetString()?.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) == true)) ||
            HasSpillMarker(document.RootElement))
            throw BlockedPlan(specification, "MongoDB sort or materialization spill");
        if (stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String &&
                                string.Equals(value.GetString(), "COLLSCAN", StringComparison.OrdinalIgnoreCase)))
            throw BlockedPlan(specification, "MongoDB collection scan");
        var matches = FindObjects(document.RootElement, "indexName")
            .Where(value => value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String && index.GetString() == physicalIndexName)
            .ToArray();
        if (matches.Length != 1 || !stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.GetString() == "IXSCAN"))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact MongoDB index scan.");
    }

    private static bool IsSqlServerSortOrMaterialization(System.Xml.Linq.XElement element)
    {
        var physicalOperation = element.Attribute("PhysicalOp")?.Value;
        return physicalOperation is not null &&
               (physicalOperation.Contains("Sort", StringComparison.OrdinalIgnoreCase) ||
                physicalOperation.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                physicalOperation.Contains("Material", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPositiveFlag(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        int.TryParse(value, out var count) && count > 0;

    private static bool HasSpillMarker(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if ((property.Name.Equals("usedDisk", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("diskUse", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("spilled", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("materialized", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Contains("spill", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Contains("materializ", StringComparison.OrdinalIgnoreCase)) &&
                    IsPositiveJsonValue(property.Value))
                    return true;
                if (HasSpillMarker(property.Value))
                    return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (HasSpillMarker(item))
                    return true;
        }
        return false;
    }

    private static bool IsPositiveJsonValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.True)
            return true;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt64(out var count) && count > 0;
        if (value.ValueKind != JsonValueKind.String)
            return false;

        var text = value.GetString() ?? string.Empty;
        return IsPositiveFlag(text) ||
               string.Equals(text, "disk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "external", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "spill", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "spilled", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument ParseJson(string content, string provider)
    {
        try { return JsonDocument.Parse(content); }
        catch (JsonException exception) { throw new PerformanceContractException($"Diagnostics {provider} native plan is invalid: {exception.Message}"); }
    }

    private static IEnumerable<JsonElement> FindObjects(JsonElement value, string requiredProperty)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty(requiredProperty, out _))
                yield return value;
            foreach (var property in value.EnumerateObject())
                foreach (var found in FindObjects(property.Value, requiredProperty))
                    yield return found;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var found in FindObjects(item, requiredProperty))
                    yield return found;
        }
    }

    private static IEnumerable<JsonElement> FindPropertyValues(JsonElement value, string propertyName)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                    yield return property.Value;
                foreach (var found in FindPropertyValues(property.Value, propertyName))
                    yield return found;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var found in FindPropertyValues(item, propertyName))
                    yield return found;
        }
    }
}
