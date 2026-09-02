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
    bool Descending = true)
{
    /// <summary>Groundwork injects this equality into every scoped query; the EF oracle isolates scopes
    /// with separate files and therefore has no synthetic scope column.</summary>
}

/// <summary>
/// Single source of truth for every scale-bearing diagnostics read executed by the frozen workload.
/// Unindexed/materializing/fanout Groundwork routes are intentionally represented with an empty index and
/// are emitted as blocked evidence without invoking their unbounded query shape; they must never be
/// converted into synthetic index-search claims. No instrument route is listed because the public
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
                route != "structured-log-replay");

        return route switch
        {
            "resources-by-last-seen" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_LastSeen", "LastSeen", null, DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "elsa_otel_resources_last_seen", "lastSeen", null, DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            "resources-by-status" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_Status", "LastSeen", "Status", DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "", "lastSeen", "status", DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            "resources-by-service" => adapter == EfAdapter
                ? Specification(EfTable, "IX_PersistedTelemetryResource_ServiceName", "LastSeen", "ServiceName", DiagnosticsDurableHistoryWorkload.ResourceCount)
                : Specification(GroundworkTable, "", "lastSeen", "serviceName", DiagnosticsDurableHistoryWorkload.ResourceCount, true),
            "traces-by-last-seen" => adapter == EfAdapter
                ? Specification("TelemetryTraces", "IX_PersistedTelemetryTrace_StartTime", "StartTime", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start", "startTime", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            // GetTraceAsync reads its summary by key, then spans and logs by trace key. The two signal
            // units have no matching secondary index, so the whole public route remains blocked.
            "trace-detail" => adapter == EfAdapter
                ? Specification("TelemetryTraces", "", null, "TraceId", DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_trace_summaries_v3", "", null, "traceKey", DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "metrics-by-last-seen" => adapter == EfAdapter
                ? Specification("MetricPoints", "", "Timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_metric_points_v2", "", "timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "logs-by-last-seen" => adapter == EfAdapter
                ? Specification("OtlpLogRecords", "", "Timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream)
                : Specification("elsa_otel_logs_v2", "", "timestamp", null, DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true),
            "structured-log-recent" => adapter == EfAdapter
                ? Specification("StructuredLogEntries", "IX_PersistedStructuredLogEntry_Sequence", "Id", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream)
                : Specification("elsa_structured_logs", "", "sequence", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream, true),
            "structured-log-replay" => adapter == EfAdapter
                ? Specification("StructuredLogEntries", "IX_PersistedStructuredLogEntry_Sequence", "Id", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream)
                : Specification("elsa_structured_logs", "", "sequence", null, DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream, true),
            _ => throw new PerformanceContractException($"Diagnostics native-plan admission does not support route '{route}'.")
        };
    }

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
            artifact.IndexName != specification.IndexName || string.IsNullOrWhiteSpace(artifact.CommandText) ||
            string.IsNullOrWhiteSpace(artifact.NativePlan))
            throw new PerformanceContractException(
                $"Diagnostics native-plan envelope does not bind route '{route.RouteIdentity}' to its exact provider, adapter, table, and index.");

        if (route.PhysicalCardinality != specification.PhysicalCardinality ||
            route.FiniteLimit != specification.FiniteLimit ||
            route.MaterializedCandidateCount != specification.FiniteLimit ||
            route.HasStorageScopePredicate != specification.StorageScopeRequired ||
            route.HasRoutePredicate != (specification.PredicateColumn is not null))
            throw new PerformanceContractException(
                $"Diagnostics native-plan route '{route.RouteIdentity}' has unbound cardinality, finite-page, or predicate facts.");

        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
            ValidateMongoCommand(artifact.CommandText, specification);
        else
            ValidateSqlCommand(artifact.CommandText, specification);

        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(artifact.NativePlan, specification);
                break;
            case "postgresql":
                ValidatePostgreSqlPlan(artifact.NativePlan, specification);
                break;
            case "sqlserver":
                ValidateSqlServerPlan(artifact.NativePlan, specification);
                break;
            case "mongodb":
                ValidateMongoPlan(artifact.NativePlan, specification);
                break;
            default:
                throw new PerformanceContractException($"Diagnostics native-plan admission does not support provider '{provider}'.");
        }
    }

    private static void ValidateSqlCommand(string command, DiagnosticsNativeRouteSpec specification)
    {
        var normalized = command.Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        if (normalized.Contains("CASE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" OR 1=1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" OR 1 = 1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" OR TRUE", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(?:LOWER|UPPER|COALESCE|CAST|SUBSTR|DATE|DATETIME)\s*\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command contains a computed or tautological predicate.");

        if (!Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(specification.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            (specification.OrderColumn is not null && !Regex.IsMatch(normalized, $@"\bORDER\s+BY\s+[\w.]*{Regex.Escape(specification.OrderColumn)}[\w.]*\s+{(specification.Descending ? "DESC" : "ASC")}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) ||
            !Regex.IsMatch(normalized, $@"\b(?:LIMIT\s+{specification.FiniteLimit}\b|TOP\s*\(?\s*{specification.FiniteLimit}\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command does not bind its exact table, descending order, and finite page.");

        if (normalized.Contains(" OR ", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(normalized, @"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && specification.StorageScopeRequired)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command contains an unbound boolean predicate.");

        var where = Regex.Match(normalized, @"\bWHERE\s+(?<where>.*?)(?:\bORDER\s+BY\b|\bLIMIT\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["where"].Value.Trim();
        var atoms = string.IsNullOrWhiteSpace(where)
            ? []
            : Regex.Split(where, @"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(atom => atom.Trim().Trim('(', ')'))
                .ToArray();
        var requiredAtoms = new List<string>();
        if (specification.StorageScopeRequired)
            requiredAtoms.Add("__groundwork_scope");
        if (specification.PredicateColumn is not null)
            requiredAtoms.Add(specification.PredicateColumn);
        if (atoms.Length != requiredAtoms.Count || requiredAtoms.Any(column =>
                atoms.Count(atom => Regex.IsMatch(atom, $@"^{Regex.Escape(column)}\s*=\s*(?:@\w+|\?|\$\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) != 1))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' command must contain only its exact equality predicates and no extra conditions.");
    }

    private static void ValidateSqlitePlan(string plan, DiagnosticsNativeRouteSpec specification)
    {
        var lines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(line => Regex.IsMatch(
                line,
                @"\b(?:USE\s+)?TEMP(?:ORARY)?\s+B[- ]TREE\b|\bMATERIAL(?:IZE|IZED|IZATION)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw BlockedPlan(specification, "SQLite sort or materialization spill");
        if (lines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\bSCAN\s+(?:{System.Text.RegularExpressions.Regex.Escape(specification.TableName)}|{System.Text.RegularExpressions.Regex.Escape(specification.TableName.Trim('"'))})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
                             !line.Contains("USING INDEX " + specification.IndexName, StringComparison.OrdinalIgnoreCase)))
            throw BlockedPlan(specification, "SQLite table scan");
        if (string.IsNullOrWhiteSpace(specification.IndexName))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' has no declared provider-native index and is blocked pending a storage redesign.");
        var search = lines.Where(line => line.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase) &&
                                        line.Contains("USING", StringComparison.OrdinalIgnoreCase) &&
                                        line.Contains("INDEX " + specification.IndexName, StringComparison.OrdinalIgnoreCase) &&
                                        (line.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) || line.Contains("SCAN", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (search.Length != 1 || !search[0].Contains(specification.IndexName, StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact '{specification.IndexName}' index search.");
    }

    private static void ValidatePostgreSqlPlan(string plan, DiagnosticsNativeRouteSpec specification)
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
                                          index.GetString() == specification.IndexName).ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact PostgreSQL index scan.");
    }

    private static void ValidateSqlServerPlan(string plan, DiagnosticsNativeRouteSpec specification)
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
                    objectElement.Attribute("Index")?.Value.Trim('[', ']') == specification.IndexName)).ToArray();
            if (matches.Length != 1)
                throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' retained plan is not the exact SQL Server index seek.");
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PerformanceContractException($"Diagnostics SQL Server native plan is invalid: {exception.Message}");
        }
    }

    private static void ValidateMongoCommand(string command, DiagnosticsNativeRouteSpec specification)
    {
        using var document = ParseJson(command, "MongoDB command");
        var root = document.RootElement;
        var collection = root.TryGetProperty("collection", out var collectionValue) && collectionValue.ValueKind == JsonValueKind.String
            ? collectionValue.GetString()
            : root.TryGetProperty("find", out var findValue) && findValue.ValueKind == JsonValueKind.String
                ? findValue.GetString()
                : null;
        if (!string.Equals(collection, specification.TableName, StringComparison.Ordinal))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' MongoDB command does not bind its exact collection.");
        if (!root.TryGetProperty("limit", out var limit) || !limit.TryGetInt32(out var finiteLimit) || finiteLimit != specification.FiniteLimit)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' MongoDB command does not bind its finite page.");
        if (specification.OrderColumn is not null &&
            (!root.TryGetProperty("sort", out var sort) || sort.ValueKind != JsonValueKind.Object ||
             !sort.TryGetProperty(specification.OrderColumn, out var direction) || !direction.TryGetInt32(out var sortDirection) || sortDirection != (specification.Descending ? -1 : 1)))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' MongoDB command does not bind descending order.");
        if (!root.TryGetProperty("filter", out var filter) || filter.ValueKind != JsonValueKind.Object)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' MongoDB command does not retain a structured filter.");
        var filterNames = filter.EnumerateObject().Select(property => property.Name).ToArray();
        var expectedNames = (specification.StorageScopeRequired ? new[] { "__groundwork_scope" } : Array.Empty<string>())
            .Concat(specification.PredicateColumn is null ? Array.Empty<string>() : [specification.PredicateColumn])
            .ToArray();
        if (!filterNames.Order(StringComparer.Ordinal).SequenceEqual(expectedNames.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            filter.EnumerateObject().Any(property =>
                property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("$eq", out var equality) ||
                property.Value.EnumerateObject().Count() != 1 ||
                equality.ValueKind == JsonValueKind.Undefined))
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' MongoDB command must retain only exact equality predicates.");
    }

    private static void ValidateMongoPlan(string plan, DiagnosticsNativeRouteSpec specification)
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
            .Where(value => value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String && index.GetString() == specification.IndexName)
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
