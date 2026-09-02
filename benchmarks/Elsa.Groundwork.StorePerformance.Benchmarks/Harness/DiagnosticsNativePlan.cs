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
    IReadOnlyList<RuntimeNativeOrderTerm>? Ordering = null)
{
    /// <summary>Relational Groundwork injects this equality into every scoped query. MongoDB isolates
    /// scopes with a provider-owned physical collection and therefore has no synthetic scope field.</summary>
    public IReadOnlyList<RuntimeNativeOrderTerm> EffectiveOrdering => Ordering ??
        (OrderColumn is null
            ? []
            : [new RuntimeNativeOrderTerm(OrderColumn, Descending ? RuntimeNativeOrderDirection.Descending : RuntimeNativeOrderDirection.Ascending)]);
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
    public const string GroundworkAdapter = "groundwork-v2";
    public const string EfAdapter = "ef-diagnostics-oracle";
    public const string EfCorrectnessOnlyRouteContract = "ef-correctness-only-unbounded-resource-routes";
    public const string BlockedRouteContract = "provider-native-routes-blocked";
    public const string GroundworkTable = "elsa_otel_resources_v2";
    public const string EfTable = "TelemetryResources";

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
                });

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

        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
            ValidateMongoCommand(artifact.CommandText, specification, artifact.NativePlan);
        else
            ValidateSqlCommand(artifact.CommandText, specification);

        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "postgresql":
                ValidatePostgreSqlPlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "sqlserver":
                ValidateSqlServerPlan(artifact.NativePlan, specification, physicalIndexName);
                break;
            case "mongodb":
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
            !string.Equals(evidence.PlanClassification, "index-search", StringComparison.Ordinal))
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
            ValidateSqlCommand(artifact.CommandText, routeSpecification);

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

    private static void ValidateSqlCommand(string command, DiagnosticsNativeRouteSpec specification)
    {
        var normalized = NormalizeSqlCommand(command);

        if (!Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(specification.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(
                normalized,
                $@"\b(?:LIMIT\s+(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)|FETCH\s+(?:FIRST|NEXT)\s+(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)\s+ROWS?|TOP\s*\(?\s*(?:{specification.FiniteLimit}\b|[@$][A-Za-z_0-9]+|\?)\s*\)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its exact table, descending order, and finite page.");

        ValidateSqlOrdering(normalized, specification);

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

        var requiredAtoms = new List<string>();
        if (specification.StorageScopeRequired)
            requiredAtoms.Add("__groundwork_scope");
        if (specification.PredicateColumn is not null)
            requiredAtoms.Add(specification.PredicateColumn);
        if (!ContainsOnlyExactEqualityPredicates(where, requiredAtoms, allowNullGuards: true))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command must contain only its exact equality predicates and no extra conditions.");
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

    private static void ValidateSqlOrdering(string command, DiagnosticsNativeRouteSpec specification)
    {
        var match = Regex.Match(command, @"\bORDER\s+BY\s+(?<order>.*?)(?:\bLIMIT\b|\bOFFSET\b|\bFETCH\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var terms = match.Groups["order"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var actual = new List<RuntimeNativeOrderTerm>(terms.Length);
        string? pendingNullOrdering = null;
        foreach (var term in terms)
        {
            var trimmed = term.Trim().TrimEnd(';');
            var nullOrdering = Regex.Match(
                trimmed,
                @"^CASE\s+WHEN\s+(?<column>[A-Za-z_][A-Za-z0-9_]*)\s+IS\s+NULL\s+THEN\s+(?:0\s+ELSE\s+1|1\s+ELSE\s+0)\s+END\s+ASC$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (nullOrdering.Success)
            {
                if (pendingNullOrdering is not null)
                    throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its complete ordered term list.");
                pendingNullOrdering = nullOrdering.Groups["column"].Value;
                continue;
            }

            var ordered = Regex.Match(
                trimmed,
                @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<column>[A-Za-z_][A-Za-z0-9_]*)\s+(?<direction>ASC|DESC)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!ordered.Success || pendingNullOrdering is not null &&
                !string.Equals(pendingNullOrdering, ordered.Groups["column"].Value, StringComparison.OrdinalIgnoreCase))
                throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its complete ordered term list.");
            pendingNullOrdering = null;
            actual.Add(new RuntimeNativeOrderTerm(
                ordered.Groups["column"].Value,
                string.Equals(ordered.Groups["direction"].Value, "DESC", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeNativeOrderDirection.Descending
                    : RuntimeNativeOrderDirection.Ascending));
        }

        if (pendingNullOrdering is not null ||
            actual.Count != specification.EffectiveOrdering.Count ||
            !actual.Zip(specification.EffectiveOrdering).All(pair =>
                string.Equals(pair.First.Column, pair.Second.Column, StringComparison.OrdinalIgnoreCase) &&
                pair.First.Direction == pair.Second.Direction))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its complete ordered term list.");
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

    private static void ValidateMongoCommand(
        string command,
        DiagnosticsNativeRouteSpec specification,
        string rawPlan)
    {
        // The observer's preview.8 text is only "MongoDB.Aggregate(page)". Reparse the retained
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

        if (isAggregate)
            ValidateMongoAggregateCommand(actual, specification);
        else
            ValidateMongoFindCommand(actual, specification);
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

    private static void ValidateMongoAggregateCommand(JsonElement command, DiagnosticsNativeRouteSpec specification)
    {
        if (!command.TryGetProperty("pipeline", out var pipeline) || pipeline.ValueKind != JsonValueKind.Array ||
            !command.TryGetProperty("cursor", out var cursor) || cursor.ValueKind != JsonValueKind.Object)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command must retain its pipeline and cursor shape.");
        if (ContainsMongoProperty(pipeline, "__groundwork_scope"))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB command must not fabricate a __groundwork_scope predicate.");

        var stages = pipeline.EnumerateArray().Where(stage => stage.ValueKind == JsonValueKind.Object).ToArray();
        var matchStages = stages.Where(stage => stage.TryGetProperty("$match", out var match) && match.ValueKind == JsonValueKind.Object)
            .Select(stage => stage.GetProperty("$match")).ToArray();
        if (matchStages.Length == 0 || !ValidateMongoRoutePredicate(matchStages[0], specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command must retain only its exact route predicate.");

        var continuationStages = matchStages.Skip(1).ToArray();
        if (continuationStages.Length != 0 &&
            (!specification.RouteIdentity.StartsWith("trace-detail/", StringComparison.Ordinal) ||
             continuationStages.Length != 1 ||
             !continuationStages[0].TryGetProperty("$or", out var keyset) ||
             !ValidateMongoContinuationFilter(keyset, specification)))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command contains an invalid continuation predicate.");

        var sortStages = stages.Where(stage => stage.TryGetProperty("$sort", out var sort) && sort.ValueKind == JsonValueKind.Object)
            .Select(stage => stage.GetProperty("$sort")).ToArray();
        if (sortStages.Length != 1 || !ValidateMongoPipelineOrdering(pipeline, sortStages[0], specification))
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command does not bind its complete effective ordering.");

        var limits = stages.Where(stage => stage.TryGetProperty("$limit", out _)).ToArray();
        if (limits.Length != 1 || !limits[0].GetProperty("$limit").TryGetInt32(out var finiteLimit) ||
            finiteLimit != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' MongoDB aggregate command does not bind its finite page limit.");
    }

    private static void ValidateMongoFindCommand(JsonElement command, DiagnosticsNativeRouteSpec specification)
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
    }

    private static bool ValidateMongoRoutePredicate(JsonElement filter, DiagnosticsNativeRouteSpec specification)
    {
        if (filter.ValueKind != JsonValueKind.Object)
            return false;
        var properties = filter.EnumerateObject().Where(property => property.Name != "$or").ToArray();
        var expected = specification.PredicateColumn is null ? Array.Empty<string>() : [specification.PredicateColumn];
        return properties.Length == expected.Length &&
               properties.Select(property => property.Name).Order(StringComparer.Ordinal)
                   .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
               properties.All(property => IsMongoEqualityValue(property.Value));
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
        if (ValidateMongoOrdering(sort, specification))
            return true;

        var expected = new List<(string Column, RuntimeNativeOrderDirection Direction)>();
        foreach (var (term, index) in specification.EffectiveOrdering.Select((term, index) => (term, index)))
        {
            expected.Add(("_groundwork_null_rank_" + index, RuntimeNativeOrderDirection.Ascending));
            expected.Add((IsMongoStringOrderColumn(term.Column) ? "_groundwork_ordinal_key_" + index : term.Column, term.Direction));
        }
        var actual = ParseMongoOrdering(sort);
        if (actual.Count != expected.Count || !actual.Zip(expected).All(pair =>
                pair.First.Column == pair.Second.Column && pair.First.Direction == pair.Second.Direction))
            return false;

        var setFields = pipeline.EnumerateArray()
            .Where(stage => stage.ValueKind == JsonValueKind.Object && stage.TryGetProperty("$set", out _))
            .SelectMany(stage => stage.GetProperty("$set").EnumerateObject().Select(property => property.Name))
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < specification.EffectiveOrdering.Count; index++)
        {
            if (!setFields.Contains($"_groundwork_null_rank_{index}") ||
                IsMongoStringOrderColumn(specification.EffectiveOrdering[index].Column) &&
                !setFields.Contains($"_groundwork_ordinal_key_{index}"))
                return false;
        }
        return true;
    }

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
}
