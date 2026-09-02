using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// The exact provider-neutral contract for bounded runtime routes captured by the adapter host. The
/// strings here are the current v2 storage declarations, not historical v1 names.
/// </summary>
public sealed record RuntimeNativeRouteSpec(
    string RouteIdentity,
    string TableName,
    string IndexName,
    IReadOnlyList<string> OrderColumns,
    IReadOnlyList<RuntimeNativePredicateSpec> Predicates,
    string? PredicateColumn,
    int PhysicalCardinality,
    int FiniteLimit,
    bool StorageScopeRequired = true)
{
    /// <summary>The provider-side page includes Groundwork's one-row continuation lookahead.</summary>
    public int NativeFetchLimit => checked(FiniteLimit + 1);
}

public sealed record RuntimeNativePredicateSpec(string Column, string Operator);

/// <summary>Retained command and normalized provider-native plan for one runtime route.</summary>
public sealed record RuntimeNativePlanArtifact(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("routeIdentity")] string RouteIdentity,
    [property: JsonPropertyName("tableName")] string TableName,
    [property: JsonPropertyName("indexName")] string IndexName,
    [property: JsonPropertyName("physicalIndexName")] string PhysicalIndexName,
    [property: JsonPropertyName("commandText")] string CommandText,
    [property: JsonPropertyName("nativePlan")] string NativePlan);

/// <summary>
/// Route truth for the current public runtime stores. A route is admitted only when the retained
/// command and provider plan prove the exact table, predicate, order, finite page and chosen index.
/// Any scan, provider sort, spill, or materialization is rejected rather than represented as an
/// optimistic summary flag.
/// </summary>
public static class RuntimeNativePlanContract
{
    public const string GroundworkAdapter = "groundwork-v2";
    public const string RouteContract = "provider-native-routes";
    private const string TriggerTable = "runtime_workflow_trigger_binding";
    private const string BookmarkTable = "runtime_bookmark_state";
    private const string SourceReferenceTable = "runtime_workflow_executable_source_reference";
    private const string PlacementTable = "elsa_distributed_execution_placement";
    private const string DurableTimerTable = "runtime_durable_timer";
    private const string RecurringScheduleTable = "runtime_recurring_trigger_schedule";
    private const string OutboxTable = "runtime_post_commit_outbox";

    public static RuntimeNativeRouteSpec For(string workloadId, string routeIdentity) => workloadId switch
    {
        RuntimeBookmarkLookupWorkload.WorkloadId => routeIdentity switch
        {
            "list-by-stimulus-and-type" => new(
                routeIdentity,
                BookmarkTable,
                "by_stimulus_and_type_and_bookmark_identity",
                ["workflowExecutionId", "bookmarkId"],
                [new("stimulusLookupKey", "=")],
                "stimulusLookupKey",
                RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow,
                RuntimeBookmarkLookupWorkload.PageSize),
            "list-by-stimulus-type" => new(
                routeIdentity,
                BookmarkTable,
                "by_stimulus_type_and_bookmark_identity",
                ["workflowExecutionId", "bookmarkId"],
                [new("stimulusTypeLookupKey", "=")],
                "stimulusTypeLookupKey",
                RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow,
                RuntimeBookmarkLookupWorkload.PageSize),
            _ => throw UnknownRoute(routeIdentity)
        },
        RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId => routeIdentity switch
        {
            "list-by-stimulus-and-type" => new(
                routeIdentity,
                TriggerTable,
                "by_stimulus_and_type",
                ["triggerBindingId"],
                [new("stimulusLookupKey", "="), new("isActive", "=")],
                "stimulusLookupKey",
                RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount * RuntimeTriggerBindingStimulusLookupWorkload.BindingsPerPublication,
                RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
            "list-by-stimulus-type" => new(
                routeIdentity,
                TriggerTable,
                "by_stimulus_type_and_active",
                ["triggerBindingId"],
                [new("stimulusTypeLookupKey", "="), new("isActive", "=")],
                "stimulusTypeLookupKey",
                RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount * RuntimeTriggerBindingStimulusLookupWorkload.BindingsPerPublication,
                RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
            "page-live-by-scope" => new(
                routeIdentity,
                SourceReferenceTable,
                "by_scope_retired_expiry_and_document_id",
                ["expiresAt", "sourceReferenceId"],
                [new("scope", "="), new("isRetired", "="), new("expiresAt", ">")],
                "scope",
                RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount,
                RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
            _ => throw UnknownRoute(routeIdentity)
        },
        DistributedPlacementTakeoverWorkload.WorkloadId => routeIdentity switch
        {
            "list-owned-live-placements" => new(
                routeIdentity,
                PlacementTable,
                "elsa_distributed_placement_owner_expiry",
                ["expiresAt", "workflowExecutionId"],
                [new("ownerId", "="), new("expiresAt", ">")],
                "ownerId",
                DistributedPlacementTakeoverWorkload.ActivePlacements,
                DistributedPlacementTakeoverWorkload.TakeoverCandidates),
            _ => throw UnknownRoute(routeIdentity)
        },
        RuntimeDueTimerSelectionWorkload.WorkloadId => routeIdentity switch
        {
            "list-due" => new(
                routeIdentity,
                DurableTimerTable,
                "by_due_time_and_timer_id",
                ["timerDueTime", "timerId"],
                [new("timerDueTime", "<=")],
                "timerDueTime",
                RuntimeDueTimerSelectionWorkload.TimerCount,
                RuntimeDueTimerSelectionWorkload.PageSize),
            _ => throw UnknownRoute(routeIdentity)
        },
        RuntimeRecurringScheduleSelectionWorkload.WorkloadId => routeIdentity switch
        {
            "list-due" => new(
                routeIdentity,
                RecurringScheduleTable,
                "by_active_next_occurrence_and_schedule_id",
                ["scheduleNextOccurrence", "scheduleId"],
                [new("scheduleIsActive", "="), new("scheduleNextOccurrence", "<=")],
                "scheduleNextOccurrence",
                RuntimeRecurringScheduleSelectionWorkload.ScheduleCount,
                RuntimeRecurringScheduleSelectionWorkload.PageSize),
            "page-by-publication" => new(
                routeIdentity,
                RecurringScheduleTable,
                "by_activation_and_schedule_id",
                ["scheduleId"],
                [new("scheduleActivationId", "=")],
                "scheduleActivationId",
                RuntimeRecurringScheduleSelectionWorkload.ScheduleCount,
                RuntimeRecurringScheduleSelectionWorkload.PageSize),
            _ => throw UnknownRoute(routeIdentity)
        },
        RuntimeOutboxDrainWorkload.WorkloadId => routeIdentity switch
        {
            "list-claimable" => new(
                routeIdentity,
                OutboxTable,
                "by_claimable_time_recorded_id",
                ["claimableAt", "outboxRecordedAt", "outboxItemId", "id"],
                [new("claimableIsEligible", "="), new("claimableAt", "<=")],
                "claimableAt",
                RuntimeOutboxDrainWorkload.OutboxEntryCount,
                RuntimeOutboxDrainWorkload.BatchSize),
            _ => throw UnknownRoute(routeIdentity)
        },
        _ => throw new PerformanceContractException(
            $"Runtime native-plan admission does not support workload '{workloadId}'.")
    };

    public static IReadOnlyList<RuntimeNativeRouteSpec> ForWorkload(string workloadId) => workloadId switch
    {
        RuntimeBookmarkLookupWorkload.WorkloadId =>
        [For(workloadId, "list-by-stimulus-and-type"), For(workloadId, "list-by-stimulus-type")],
        RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId =>
        [For(workloadId, "list-by-stimulus-and-type"), For(workloadId, "list-by-stimulus-type"), For(workloadId, "page-live-by-scope")],
        DistributedPlacementTakeoverWorkload.WorkloadId => [For(workloadId, "list-owned-live-placements")],
        RuntimeDueTimerSelectionWorkload.WorkloadId => [For(workloadId, "list-due")],
        RuntimeRecurringScheduleSelectionWorkload.WorkloadId => [For(workloadId, "list-due"), For(workloadId, "page-by-publication")],
        RuntimeOutboxDrainWorkload.WorkloadId => [For(workloadId, "list-claimable")],
        _ => throw new PerformanceContractException($"Runtime native-plan admission does not support workload '{workloadId}'.")
    };

    /// <summary>Maps the declared logical index to the provider-owned physical index name.</summary>
    public static string ExpectedPhysicalIndexName(string provider, RuntimeNativeRouteSpec specification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(specification);
        var composed = $"__groundwork_ix_{specification.TableName.Length}_{specification.TableName}_{specification.IndexName.Length}_{specification.IndexName}";
        return provider switch
        {
            "mongodb" => specification.IndexName,
            "sqlite" => composed,
            "postgresql" => TruncatePhysicalIndex(composed, 63, 10),
            "sqlserver" => TruncatePhysicalIndex(composed, 128, 12),
            _ => throw new PerformanceContractException(
                $"Runtime native-plan admission does not support provider '{provider}'.")
        };
    }

    /// <summary>
    /// Revalidates a retained envelope at correctness admission. Summary booleans are deliberately not
    /// trusted: the command and native plan are parsed again from the retained artifact.
    /// </summary>
    public static void ValidateEnvelope(
        string workloadId,
        string provider,
        string adapter,
        NativeRouteEvidence route,
        string path)
    {
        ArgumentNullException.ThrowIfNull(route);
        RuntimeNativePlanArtifact artifact;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            ArtifactStore.RejectDuplicateProperties(document.RootElement);
            artifact = JsonSerializer.Deserialize<RuntimeNativePlanArtifact>(
                           document.RootElement.GetRawText(),
                           ArtifactStore.JsonOptions)
                       ?? throw new PerformanceContractException("Runtime native-plan envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Runtime native-plan envelope is invalid: {exception.Message}");
        }

        var specification = For(workloadId, route.RouteIdentity);
        var physicalIndex = ExpectedPhysicalIndexName(provider, specification);
        if (artifact.SchemaVersion != 1 ||
            !string.Equals(artifact.Provider, provider, StringComparison.Ordinal) ||
            !string.Equals(artifact.Adapter, adapter, StringComparison.Ordinal) ||
            !string.Equals(artifact.RouteIdentity, route.RouteIdentity, StringComparison.Ordinal) ||
            !string.Equals(artifact.TableName, specification.TableName, StringComparison.Ordinal) ||
            !string.Equals(artifact.IndexName, specification.IndexName, StringComparison.Ordinal) ||
            !string.Equals(artifact.PhysicalIndexName, physicalIndex, StringComparison.Ordinal) ||
            !string.Equals(route.IndexName, physicalIndex, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(artifact.CommandText) ||
            string.IsNullOrWhiteSpace(artifact.NativePlan))
        {
            throw new PerformanceContractException(
                $"Runtime native-plan envelope does not bind route '{route.RouteIdentity}' to its exact provider, adapter, table and index.");
        }

        if (route.PhysicalCardinality != specification.PhysicalCardinality ||
            !string.Equals(route.PlanClassification, "index-search", StringComparison.Ordinal) ||
            route.FiniteLimit != specification.FiniteLimit ||
            route.NativeFetchLimit != specification.NativeFetchLimit ||
            route.MaterializedCandidateCount != specification.FiniteLimit ||
            route.HasStorageScopePredicate != specification.StorageScopeRequired ||
            route.HasRoutePredicate != (specification.PredicateColumn is not null))
        {
            throw new PerformanceContractException(
                $"Runtime native-plan route '{route.RouteIdentity}' has unbound cardinality, bounded-page, or predicate facts.");
        }

        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
            ValidateMongoCommand(artifact.CommandText, specification);
        else
            ValidateSqlCommand(artifact.CommandText, specification);

        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(artifact.NativePlan, specification, route.IndexName);
                break;
            case "postgresql":
                ValidatePostgreSqlPlan(artifact.NativePlan, specification, route.IndexName);
                break;
            case "sqlserver":
                ValidateSqlServerPlan(artifact.NativePlan, specification, route.IndexName);
                break;
            case "mongodb":
                ValidateMongoPlan(artifact.NativePlan, specification, route.IndexName);
                break;
            default:
                throw new PerformanceContractException($"Runtime native-plan admission does not support provider '{provider}'.");
        }
    }

    private static Exception UnknownRoute(string route) =>
        new PerformanceContractException($"Runtime native-plan admission does not support route '{route}'.");

    private static void ValidateSqlCommand(string command, RuntimeNativeRouteSpec specification)
    {
        var normalized = command.Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        normalized = Regex.Replace(
            normalized,
            @"\s+COLLATE\s+[A-Za-z_][A-Za-z0-9_.]*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (Regex.IsMatch(normalized, @"\b(?:DISTINCT|GROUP\s+BY|OVER|ROW_NUMBER|UNION|OFFSET)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw InvalidCommand(specification, "a bounded, non-materializing page shape");

        const string parameter = @"(?:[@$][A-Za-z_0-9]+|\?)";
        var limit = $@"(?:{specification.NativeFetchLimit}|{parameter})";
        if (!Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(specification.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(
                normalized,
                $@"\b(?:LIMIT\s+{limit}|FETCH\s+(?:FIRST|NEXT)\s+{limit}\s+ROWS?|TOP\s*\(?\s*{limit}\s*\)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw InvalidCommand(specification, "exact table or native lookahead limit");

        var order = Regex.Match(
            normalized,
            @"\bORDER\s+BY\s+(?<order>.*?)(?:\bLIMIT\b|\bFETCH\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline).Groups["order"].Value;
        var allowedOrderColumns = specification.OrderColumns.Append("id").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderTerms = order.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var orderColumns = new List<string>(orderTerms.Length);
        string? pendingNullOrdering = null;
        foreach (var term in orderTerms)
        {
            var trimmed = term.Trim().TrimEnd(';');
            var nullOrdering = Regex.Match(
                trimmed,
                @"^CASE\s+WHEN\s+(?<column>[A-Za-z_][A-Za-z0-9_]*)\s+IS\s+NULL\s+THEN\s+(?:0\s+ELSE\s+1|1\s+ELSE\s+0)\s+END\s+ASC$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (nullOrdering.Success)
            {
                var column = nullOrdering.Groups["column"].Value;
                if (pendingNullOrdering is not null || !allowedOrderColumns.Contains(column))
                    throw InvalidCommand(specification, "exact deterministic ordering");
                pendingNullOrdering = column;
                continue;
            }

            var match = Regex.Match(
                trimmed,
                @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<column>[A-Za-z_][A-Za-z0-9_]*)\s+ASC$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                throw InvalidCommand(specification, $"exact deterministic ordering; unexpected term '{term.Trim()}'");
            var orderedColumn = match.Groups["column"].Value;
            if (pendingNullOrdering is not null &&
                !string.Equals(pendingNullOrdering, orderedColumn, StringComparison.OrdinalIgnoreCase))
                throw InvalidCommand(specification, "null ordering immediately paired with its declared column");
            pendingNullOrdering = null;
            orderColumns.Add(orderedColumn);
        }
        if (pendingNullOrdering is not null)
            throw InvalidCommand(specification, "complete deterministic ordering");
        var expectedOrder = specification.OrderColumns;
        if (!expectedOrder.SequenceEqual(orderColumns, StringComparer.OrdinalIgnoreCase) &&
            !(orderColumns.Count == expectedOrder.Count + 1 &&
              expectedOrder.SequenceEqual(orderColumns.Take(expectedOrder.Count), StringComparer.OrdinalIgnoreCase) &&
              string.Equals(orderColumns[^1], "id", StringComparison.OrdinalIgnoreCase)))
            throw InvalidCommand(specification, "exact deterministic ordering");

        var whereMatch = Regex.Match(
            normalized,
            @"\bWHERE\s+(?<where>.*?)(?:\bORDER\s+BY\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!whereMatch.Success)
            throw InvalidCommand(specification, "the exact route predicates");
        var where = whereMatch.Groups["where"].Value;
        if (Regex.IsMatch(where, @"\b(?:OR|SELECT|CASE|LOWER|UPPER|COALESCE|CAST|SUBSTR|DATE|DATETIME)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw InvalidCommand(specification, "only direct conjunctive predicates");

        const string value = @"(?:[@$][A-Za-z_0-9]+|\?|[-+]?\d+(?:\.\d+)?|'[^']*'|TRUE|FALSE)";
        var requiredPredicates = specification.Predicates.ToList();
        if (specification.StorageScopeRequired &&
            !Regex.IsMatch(
                where,
                $@"\b__groundwork_scope\b\s*=\s*{value}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw InvalidCommand(specification, "the provider storage-scope predicate");
        if (specification.StorageScopeRequired)
            requiredPredicates.Insert(0, new RuntimeNativePredicateSpec("__groundwork_scope", "="));
        foreach (var predicate in specification.Predicates)
        {
            if (!Regex.IsMatch(
                    where,
                    $@"\b{Regex.Escape(predicate.Column)}\b\s*{Regex.Escape(predicate.Operator)}\s*{value}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw InvalidCommand(specification, $"predicate '{predicate.Column} {predicate.Operator}'");
        }

        var remainder = where;
        foreach (var predicate in requiredPredicates)
        {
            remainder = Regex.Replace(
                remainder,
                $@"\b{Regex.Escape(predicate.Column)}\b\s+IS\s+NOT\s+NULL",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            remainder = Regex.Replace(
                remainder,
                $@"\b{Regex.Escape(predicate.Column)}\b\s*{Regex.Escape(predicate.Operator)}\s*{value}",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        remainder = Regex.Replace(remainder, @"\bAND\b|[();\s]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (remainder.Length != 0)
            throw InvalidCommand(specification, "only the declared route predicates and their null guards");
    }

    private static void ValidateMongoCommand(string command, RuntimeNativeRouteSpec specification)
    {
        using var document = ParseJson(command, "MongoDB command");
        var root = document.RootElement;
        var collection = root.TryGetProperty("collection", out var collectionValue) && collectionValue.ValueKind == JsonValueKind.String
            ? collectionValue.GetString()
            : root.TryGetProperty("find", out var findValue) && findValue.ValueKind == JsonValueKind.String
                ? findValue.GetString()
                : null;
        if (!string.Equals(collection, specification.TableName, StringComparison.Ordinal))
            throw InvalidCommand(specification, "exact collection");
        if (!root.TryGetProperty("limit", out var limit) || !TryFiniteLimit(limit, specification.NativeFetchLimit))
            throw InvalidCommand(specification, "exact native lookahead limit");
        if (!root.TryGetProperty("sort", out var sort) || sort.ValueKind != JsonValueKind.Object ||
            !SortMatches(sort, specification.OrderColumns))
            throw InvalidCommand(specification, "exact deterministic ordering");
        if (!root.TryGetProperty("filter", out var filter) || filter.ValueKind != JsonValueKind.Object)
            throw InvalidCommand(specification, "structured predicates");
        var names = filter.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var requiredNames = specification.Predicates.Select(predicate => predicate.Column).ToHashSet(StringComparer.Ordinal);
        if (specification.StorageScopeRequired)
            requiredNames.Add("__groundwork_scope");
        if (!names.SetEquals(requiredNames))
            throw InvalidCommand(specification, "only its exact structured predicates");
        if (specification.StorageScopeRequired &&
            (!filter.TryGetProperty("__groundwork_scope", out var scope) ||
             !MongoPredicateMatches(scope, "=")))
            throw InvalidCommand(specification, "the provider storage-scope predicate");
        foreach (var predicate in specification.Predicates)
        {
            if (!filter.TryGetProperty(predicate.Column, out var value) ||
                !MongoPredicateMatches(value, predicate.Operator))
                throw InvalidCommand(specification, $"predicate '{predicate.Column} {predicate.Operator}'");
        }
    }

    private static void ValidateSqlitePlan(string plan, RuntimeNativeRouteSpec specification, string physicalIndex)
    {
        var lines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unsafeLine = lines.FirstOrDefault(line => Regex.IsMatch(line, @"\b(?:USE\s+)?TEMP(?:ORARY)?\s+B[- ]TREE\b|\b(?:MATERIAL|MATERIALIZE|MATERIALIZED)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (unsafeLine is not null)
            throw InvalidPlan(specification, $"SQLite sort or materialization spill ('{unsafeLine}')");
        if (lines.Any(line => Regex.IsMatch(line, @"\bSCAN\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                             !Regex.IsMatch(line, @"\bSCAN\s+__(?:groundwork_total|groundwork_page)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw InvalidPlan(specification, "SQLite physical scan");
        var searches = lines.Where(line => Regex.IsMatch(line, @"\bSEARCH\b.*\bUSING\s+(?:COVERING\s+)?INDEX\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (searches.Length == 0 || searches.Any(line => !line.Contains(physicalIndex, StringComparison.Ordinal)))
            throw InvalidPlan(specification, $"the exact SQLite index '{physicalIndex}'");
    }

    private static void ValidatePostgreSqlPlan(string plan, RuntimeNativeRouteSpec specification, string physicalIndex)
    {
        using var document = ParseJson(plan, "PostgreSQL plan");
        var nodes = FindObjects(document.RootElement, "Node Type").ToArray();
        if (nodes.Any(node => node.TryGetProperty("Node Type", out var kind) && kind.ValueKind == JsonValueKind.String &&
                              (kind.GetString()?.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase) == true ||
                               kind.GetString()?.Contains("Sort", StringComparison.OrdinalIgnoreCase) == true ||
                               kind.GetString()?.Contains("Materialize", StringComparison.OrdinalIgnoreCase) == true)) ||
            FindObjects(document.RootElement, "Sort Method").Any() || HasSpillMarker(document.RootElement))
            throw InvalidPlan(specification, "PostgreSQL scan, sort, spill, or materialization");
        var matches = nodes.Where(node => node.TryGetProperty("Node Type", out var kind) && kind.ValueKind == JsonValueKind.String &&
                                          kind.GetString() is "Index Scan" or "Index Only Scan" &&
                                          node.TryGetProperty("Relation Name", out var relation) &&
                                          relation.ValueKind == JsonValueKind.String &&
                                          relation.GetString() == specification.TableName &&
                                          node.TryGetProperty("Index Name", out var index) && index.ValueKind == JsonValueKind.String &&
                                          index.GetString() == physicalIndex).ToArray();
        if (matches.Length != 1)
            throw InvalidPlan(specification, $"the exact PostgreSQL index '{physicalIndex}'");
    }

    private static void ValidateSqlServerPlan(string plan, RuntimeNativeRouteSpec specification, string physicalIndex)
    {
        XDocument document;
        try { document = XDocument.Parse(plan, LoadOptions.PreserveWhitespace); }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        { throw InvalidPlan(specification, $"valid SQL Server showplan XML ({exception.Message})"); }
        if (document.Descendants().Any(element => element.Name.LocalName.Contains("Sort", StringComparison.OrdinalIgnoreCase) ||
                                                  element.Name.LocalName.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                                                  element.Name.LocalName.Contains("Material", StringComparison.OrdinalIgnoreCase) ||
                                                  element.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) &&
                                                  IsPositiveXmlValue(element.Value) ||
                                                  element.Attributes().Any(attribute =>
                                                      attribute.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) &&
                                                      IsPositiveXmlValue(attribute.Value))) ||
            document.Descendants().Where(element => element.Name.LocalName == "RelOp")
                .Any(element => ((string?)element.Attribute("PhysicalOp"))?.Contains("Scan", StringComparison.OrdinalIgnoreCase) == true))
            throw InvalidPlan(specification, "SQL Server scan, sort, spill, or materialization");
        var matches = document.Descendants().Where(element => element.Name.LocalName == "RelOp" &&
                string.Equals((string?)element.Attribute("PhysicalOp"), "Index Seek", StringComparison.Ordinal))
            .Where(element => element.Descendants().Any(child => child.Name.LocalName == "Object" &&
                string.Equals(((string?)child.Attribute("Table"))?.Trim('[', ']'), specification.TableName, StringComparison.Ordinal) &&
                string.Equals(((string?)child.Attribute("Index"))?.Trim('[', ']'), physicalIndex, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length != 1)
            throw InvalidPlan(specification, $"the exact SQL Server index '{physicalIndex}'");
    }

    private static void ValidateMongoPlan(string plan, RuntimeNativeRouteSpec specification, string physicalIndex)
    {
        using var document = ParseJson(plan, "MongoDB plan");
        var stages = FindObjects(document.RootElement, "stage").ToArray();
        if (stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String &&
                                (value.GetString()?.Contains("COLLSCAN", StringComparison.OrdinalIgnoreCase) == true ||
                                 value.GetString()?.Contains("SORT", StringComparison.OrdinalIgnoreCase) == true ||
                                 value.GetString()?.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) == true)) ||
            HasSpillMarker(document.RootElement))
            throw InvalidPlan(specification, "MongoDB collection scan, sort, spill, or materialization");
        var matches = FindObjects(document.RootElement, "indexName")
            .Where(value => value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String && index.GetString() == physicalIndex)
            .ToArray();
        if (matches.Length != 1 || !stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.GetString() == "IXSCAN"))
            throw InvalidPlan(specification, $"the exact MongoDB index '{physicalIndex}'");
    }

    private static bool SortMatches(JsonElement sort, IReadOnlyList<string> columns)
    {
        var actual = sort.EnumerateObject().ToArray();
        return actual.Length == columns.Count && actual.Select(property => property.Name).SequenceEqual(columns, StringComparer.Ordinal) &&
               actual.All(property => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var direction) && direction == 1);
    }

    private static bool MongoPredicateMatches(JsonElement value, string operation)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return operation == "=" && value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
        var expected = operation switch
        {
            ">" => "$gt",
            ">=" => "$gte",
            "<" => "$lt",
            "<=" => "$lte",
            _ => "$eq"
        };
        return value.EnumerateObject().Count() == 1 && value.TryGetProperty(expected, out _);
    }

    private static string TruncatePhysicalIndex(string composed, int maximumLength, int digestLength) =>
        composed.Length <= maximumLength
            ? composed
            : composed[..(maximumLength - digestLength - 1)] + "_" +
              Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composed)))[..digestLength].ToLowerInvariant();

    private static bool TryFiniteLimit(JsonElement value, int expected) =>
        value.TryGetInt32(out var actual) && actual == expected;

    private static JsonDocument ParseJson(string content, string description)
    {
        try { return JsonDocument.Parse(content); }
        catch (JsonException exception) { throw new PerformanceContractException($"Runtime {description} is invalid JSON: {exception.Message}"); }
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

    private static bool HasSpillMarker(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if ((property.Name.Contains("spill", StringComparison.OrdinalIgnoreCase) ||
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
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
               int.TryParse(text, out var textCount) && textCount > 0 ||
               string.Equals(text, "disk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "external", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "spill", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "spilled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositiveXmlValue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        int.TryParse(value, out var count) && count > 0;

    private static PerformanceContractException InvalidCommand(RuntimeNativeRouteSpec specification, string detail) =>
        new($"Runtime route '{specification.RouteIdentity}' command does not prove {detail}.");

    private static PerformanceContractException InvalidPlan(RuntimeNativeRouteSpec specification, string detail) =>
        new($"Runtime route '{specification.RouteIdentity}' native plan does not prove {detail}.");
}
