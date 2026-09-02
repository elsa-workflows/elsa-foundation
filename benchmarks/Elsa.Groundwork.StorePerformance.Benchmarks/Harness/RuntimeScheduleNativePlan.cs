using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Frozen native-plan contract for the bounded schedule-selection routes.
///
/// The retained artifact contains the exact provider command as well as the provider's explain output.
/// Admission revalidates both instead of trusting route summary booleans. This contract deliberately
/// rejects scans, sort/materialization operators, and spill markers: a bounded result count alone is not
/// evidence that a provider avoided doing scale-bearing work outside the declared index.
/// </summary>
public static class RuntimeScheduleNativePlan
{
    private const string Magic = "GROUNDWORK-SCHEDULE-NATIVE-PLAN/1";
    private const string CommandSeparator = "---provider-command---";
    private const string PlanSeparator = "---provider-plan---";

    private static readonly Regex SqliteIndexSearch = new(
        @"\bSEARCH\b[^\r\n]*\bUSING\s+(?:COVERING\s+)?INDEX\s+[\x22'`\[]?(?<index>[^\s\x22'`()]+)[\x22'`\]]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record RouteDefinition(
        string WorkloadId,
        string RouteIdentity,
        string TableName,
        string IndexName,
        IReadOnlyList<string> PredicateFields,
        IReadOnlyList<string> PredicateOperators,
        IReadOnlyList<string> OrderFields,
        int PhysicalCardinality,
        int FiniteLimit,
        int MaterializedCandidateCount);

    public static RouteDefinition Definition(string workloadId, string routeIdentity) =>
        (workloadId, routeIdentity) switch
        {
            ("due-timer-selection", "list-due") => new(
                workloadId,
                routeIdentity,
                "runtime_durable_timer",
                "by_due_time_and_timer_id",
                ["timerDueTime"],
                ["<="],
                ["timerDueTime", "timerId"],
                RuntimeDueTimerSelectionWorkload.TimerCount,
                RuntimeDueTimerSelectionWorkload.PageSize,
                RuntimeDueTimerSelectionWorkload.PageSize),
            ("recurring-schedule-selection", "list-due") => new(
                workloadId,
                routeIdentity,
                "runtime_recurring_trigger_schedule",
                "by_active_next_occurrence_and_schedule_id",
                ["scheduleIsActive", "scheduleNextOccurrence"],
                ["=", "<="],
                ["scheduleNextOccurrence", "scheduleId"],
                RuntimeRecurringScheduleSelectionWorkload.ScheduleCount,
                RuntimeRecurringScheduleSelectionWorkload.PageSize,
                RuntimeRecurringScheduleSelectionWorkload.PageSize),
            ("recurring-schedule-selection", "page-by-publication") => new(
                workloadId,
                routeIdentity,
                "runtime_recurring_trigger_schedule",
                "by_activation_and_schedule_id",
                ["activationId"],
                ["="],
                ["scheduleId"],
                RuntimeRecurringScheduleSelectionWorkload.ScheduleCount,
                RuntimeRecurringScheduleSelectionWorkload.PageSize,
                RuntimeRecurringScheduleSelectionWorkload.PageSize),
            _ => throw new PerformanceContractException(
                $"Schedule native-plan admission does not recognize route '{workloadId}/{routeIdentity}'.")
        };

    public static string Create(
        string provider,
        string workloadId,
        string routeIdentity,
        string providerCommand,
        string providerPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPlan);
        var definition = Definition(workloadId, routeIdentity);
        return string.Join('\n',
            Magic,
            $"provider={provider}",
            $"workload={definition.WorkloadId}",
            $"route={definition.RouteIdentity}",
            $"table={definition.TableName}",
            $"index={definition.IndexName}",
            $"predicate-fields={string.Join(',', definition.PredicateFields)}",
            $"predicate-operators={string.Join(',', definition.PredicateOperators)}",
            $"order={string.Join(',', definition.OrderFields)}",
            $"physical-cardinality={definition.PhysicalCardinality.ToString(CultureInfo.InvariantCulture)}",
            $"finite-limit={definition.FiniteLimit.ToString(CultureInfo.InvariantCulture)}",
            $"materialized-candidates={definition.MaterializedCandidateCount.ToString(CultureInfo.InvariantCulture)}",
            CommandSeparator,
            providerCommand,
            PlanSeparator,
            providerPlan);
    }

    public static void Validate(string provider, NativeRouteEvidence route, string retained)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(route);
        var envelope = Parse(retained);
        var definition = Definition(envelope.WorkloadId, route.RouteIdentity);

        if (!string.Equals(envelope.Provider, provider, StringComparison.Ordinal) ||
            !string.Equals(envelope.WorkloadId, definition.WorkloadId, StringComparison.Ordinal) ||
            !string.Equals(envelope.RouteIdentity, definition.RouteIdentity, StringComparison.Ordinal) ||
            !string.Equals(envelope.TableName, definition.TableName, StringComparison.Ordinal) ||
            !string.Equals(envelope.IndexName, definition.IndexName, StringComparison.Ordinal) ||
            !string.Equals(envelope.PredicateFields, string.Join(',', definition.PredicateFields), StringComparison.Ordinal) ||
            !string.Equals(envelope.PredicateOperators, string.Join(',', definition.PredicateOperators), StringComparison.Ordinal) ||
            !string.Equals(envelope.OrderFields, string.Join(',', definition.OrderFields), StringComparison.Ordinal) ||
            envelope.PhysicalCardinality != definition.PhysicalCardinality ||
            envelope.FiniteLimit != definition.FiniteLimit ||
            envelope.MaterializedCandidateCount != definition.MaterializedCandidateCount ||
            route.PhysicalCardinality != definition.PhysicalCardinality ||
            route.FiniteLimit != definition.FiniteLimit ||
            route.MaterializedCandidateCount != definition.MaterializedCandidateCount ||
            route.PlanClassification != "index-search" ||
            !string.Equals(route.IndexName, ExpectedPhysicalIndexName(provider, definition), StringComparison.Ordinal) ||
            !route.HasStorageScopePredicate ||
            !route.HasRoutePredicate)
        {
            throw new PerformanceContractException(
                "Schedule retained native plan does not bind the frozen route, table, index, predicates, order, cardinality, finite limit, and materialized count.");
        }

        ValidateCommand(provider, envelope.ProviderCommand, definition);
        ValidatePlan(provider, envelope.ProviderPlan, definition, route.IndexName);
    }

    internal static string ProviderPlanForStructuredSafetyValidation(string content) =>
        content.StartsWith(Magic, StringComparison.Ordinal) ? Parse(content).ProviderPlan : content;

    /// <summary>Maps the frozen logical index to the provider-owned physical index name.</summary>
    public static string ExpectedPhysicalIndexName(string provider, RouteDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(definition);
        var composed = $"__groundwork_ix_{definition.TableName.Length}_{definition.TableName}_{definition.IndexName.Length}_{definition.IndexName}";
        return provider switch
        {
            "mongodb" => definition.IndexName,
            "sqlite" => composed,
            "postgresql" => composed.Length <= 63
                ? composed
                : composed[..(63 - 11)] + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composed)))[..10].ToLowerInvariant(),
            "sqlserver" => composed.Length <= 128
                ? composed
                : composed[..(128 - 13)] + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composed)))[..12].ToLowerInvariant(),
            _ => throw new PerformanceContractException(
                $"Schedule native-plan admission does not support provider '{provider}'.")
        };
    }

    private static void ValidateCommand(string provider, string command, RouteDefinition definition)
    {
        if (string.Equals(provider, "mongodb", StringComparison.Ordinal))
        {
            ValidateMongoCommand(command, definition);
            return;
        }

        var normalized = command.Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        if (!Regex.IsMatch(normalized, $@"\bFROM\s+{Regex.Escape(definition.TableName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(normalized, @"\b(?:LIMIT|FETCH|TOP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            normalized.Contains(" OFFSET ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" DISTINCT ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" GROUP BY ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" OVER ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" ROW_NUMBER", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" UNION ", StringComparison.OrdinalIgnoreCase))
        {
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' command does not retain its exact table, finite page, or bounded query shape.");
        }

        var orderStart = normalized.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (orderStart < 0 || normalized[orderStart..].Contains(" DESC", StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' command does not retain ascending deterministic ordering.");
        var previousOrderPosition = orderStart;
        foreach (var field in definition.OrderFields)
        {
            var next = normalized.IndexOf(field, previousOrderPosition, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' command does not retain order field '{field}'.");
            previousOrderPosition = next + field.Length;
        }

        var whereStart = normalized.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        var orderEnd = normalized.IndexOf("ORDER BY", whereStart + 1, StringComparison.OrdinalIgnoreCase);
        var whereClause = whereStart < 0
            ? string.Empty
            : normalized[whereStart..(orderEnd < 0 ? normalized.Length : orderEnd)];
        const string value = """(?:[@?$]\w*|\?|\d+(?:\.\d+)?|'[^']*'|"[^"]*")""";
        if (whereStart < 0 || !Regex.IsMatch(
                whereClause,
                $@"\b__groundwork_scope\b\s*=\s*{value}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' command does not retain the storage-scope predicate.");
        foreach (var (field, operation) in definition.PredicateFields.Zip(definition.PredicateOperators))
        {
            if (!Regex.IsMatch(
                    whereClause,
                    $@"\b{Regex.Escape(field)}\b\s*{Regex.Escape(operation)}\s*{value}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' command does not retain predicate '{field} {operation}'.");
        }
    }

    private static void ValidateMongoCommand(string command, RouteDefinition definition)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(command);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"Schedule MongoDB command is invalid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            var collection = root.TryGetProperty("collection", out var collectionValue) && collectionValue.ValueKind == JsonValueKind.String
                ? collectionValue.GetString()
                : root.TryGetProperty("find", out var findValue) && findValue.ValueKind == JsonValueKind.String
                    ? findValue.GetString()
                    : null;
            if (!string.Equals(collection, definition.TableName, StringComparison.Ordinal) ||
                !root.TryGetProperty("limit", out var limit) ||
                !limit.TryGetInt32(out var finiteLimit) || finiteLimit != definition.FiniteLimit ||
                root.TryGetProperty("skip", out _))
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' MongoDB command does not retain its exact collection and finite keyset page.");

            if (!root.TryGetProperty("sort", out var sort) || sort.ValueKind != JsonValueKind.Object)
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' MongoDB command does not retain deterministic ordering.");
            var sortFields = sort.EnumerateObject().Select(property => property.Name).ToArray();
            if (!sortFields.SequenceEqual(definition.OrderFields, StringComparer.Ordinal) ||
                sort.EnumerateObject().Any(property => !property.Value.TryGetInt32(out var direction) || direction != 1))
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' MongoDB command does not retain ascending deterministic ordering.");

            if (!root.TryGetProperty("filter", out var filter) || filter.ValueKind != JsonValueKind.Object)
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' MongoDB command does not retain a structured filter.");
            var expectedNames = new[] { "__groundwork_scope" }.Concat(definition.PredicateFields).Order(StringComparer.Ordinal).ToArray();
            var actualNames = filter.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
            if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
                throw new PerformanceContractException(
                    $"Schedule route '{definition.RouteIdentity}' MongoDB command contains an unexpected filter shape.");
            // Mongo's due comparison is the only non-equality predicate. Its serialized command must
            // retain the operator in the exact filter field; equality predicates remain $eq. Every
            // predicate object is required to contain exactly one operator so an additional range or
            // computed condition cannot hide in the command envelope.
            foreach (var property in filter.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object || property.Value.EnumerateObject().Count() != 1)
                    throw new PerformanceContractException(
                        $"Schedule route '{definition.RouteIdentity}' MongoDB command must retain exactly one operator per predicate.");

                var expectedOperator = property.Name == "__groundwork_scope"
                    ? "$eq"
                    : definition.PredicateFields
                        .Zip(definition.PredicateOperators)
                        .Where(pair => pair.First == property.Name)
                        .Select(pair => pair.Second == "<=" ? "$lte" : "$eq")
                        .SingleOrDefault();
                if (expectedOperator is null || !property.Value.TryGetProperty(expectedOperator, out _))
                    throw new PerformanceContractException(
                        $"Schedule route '{definition.RouteIdentity}' MongoDB command has an unexpected predicate operator for '{property.Name}'.");
            }
        }
    }

    private static void ValidatePlan(
        string provider,
        string plan,
        RouteDefinition definition,
        string physicalIndexName)
    {
        switch (provider)
        {
            case "sqlite":
                ValidateSqlitePlan(plan, definition, physicalIndexName);
                break;
            case "postgresql":
                ValidatePostgreSqlPlan(plan, definition, physicalIndexName);
                break;
            case "sqlserver":
                ValidateSqlServerPlan(plan, definition, physicalIndexName);
                break;
            case "mongodb":
                ValidateMongoPlan(plan, definition, physicalIndexName);
                break;
            default:
                throw new PerformanceContractException(
                    $"Schedule native-plan admission does not support provider '{provider}'.");
        }
    }

    private static void ValidateSqlitePlan(string plan, RouteDefinition definition, string physicalIndexName)
    {
        var lines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(line => Regex.IsMatch(
                line,
                @"\b(?:SCAN|SORT|MATERIAL(?:IZE|IZED|IZATION)|SPILL)\b|\bUSE\s+TEMP(?:ORARY)?\s+B[- ]TREE\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a SQLite scan, sort, materialization, or spill.");

        var matches = SqliteIndexSearch.Matches(plan)
            .Select(match => match.Groups["index"].Value.Trim('"', '`', '[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1 || !string.Equals(matches[0], physicalIndexName, StringComparison.Ordinal) ||
            !lines.Any(line => line.Contains(definition.TableName, StringComparison.OrdinalIgnoreCase) &&
                               line.Contains(physicalIndexName, StringComparison.OrdinalIgnoreCase)))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained plan is not the exact '{physicalIndexName}' index search.");
    }

    private static void ValidatePostgreSqlPlan(string plan, RouteDefinition definition, string physicalIndexName)
    {
        using var document = ParseJson(plan, "PostgreSQL");
        var nodes = FindObjects(document.RootElement, "Node Type").ToArray();
        if (nodes.Any(node => node.TryGetProperty("Node Type", out var nodeType) && nodeType.ValueKind == JsonValueKind.String &&
                             (nodeType.GetString()?.Contains("Sort", StringComparison.OrdinalIgnoreCase) == true ||
                              nodeType.GetString()?.Contains("Material", StringComparison.OrdinalIgnoreCase) == true ||
                              nodeType.GetString()?.Contains("Spill", StringComparison.OrdinalIgnoreCase) == true)) ||
            FindObjects(document.RootElement, "Sort Method").Any() ||
            HasSpillMarker(document.RootElement))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a PostgreSQL sort, materialization, or spill.");
        if (nodes.Any(node => node.TryGetProperty("Node Type", out var nodeType) && nodeType.ValueKind == JsonValueKind.String &&
                             nodeType.GetString()?.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase) == true ||
                             node.TryGetProperty("Node Type", out nodeType) && nodeType.ValueKind == JsonValueKind.String &&
                             nodeType.GetString()?.Contains("Bitmap Heap Scan", StringComparison.OrdinalIgnoreCase) == true))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a PostgreSQL table scan.");

        var matches = nodes.Where(node => node.TryGetProperty("Node Type", out var nodeType) && nodeType.ValueKind == JsonValueKind.String &&
                                          nodeType.GetString() is "Index Scan" or "Index Only Scan" &&
                                          node.TryGetProperty("Relation Name", out var relation) && relation.ValueKind == JsonValueKind.String &&
                                          relation.GetString() == definition.TableName &&
                                          node.TryGetProperty("Index Name", out var index) && index.ValueKind == JsonValueKind.String &&
                                          index.GetString() == physicalIndexName).ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained plan is not the exact PostgreSQL index scan.");
    }

    private static void ValidateSqlServerPlan(string plan, RouteDefinition definition, string physicalIndexName)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(plan, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PerformanceContractException(
                $"Schedule SQL Server native plan is invalid XML: {exception.Message}");
        }

        var relops = document.Descendants().Where(element => element.Name.LocalName == "RelOp").ToArray();
        if (relops.Any(element =>
                element.Attribute("PhysicalOp")?.Value.Contains("Scan", StringComparison.OrdinalIgnoreCase) == true ||
                element.Attribute("PhysicalOp")?.Value.Contains("Sort", StringComparison.OrdinalIgnoreCase) == true ||
                element.Attribute("PhysicalOp")?.Value.Contains("Spool", StringComparison.OrdinalIgnoreCase) == true ||
                element.Attribute("PhysicalOp")?.Value.Contains("Material", StringComparison.OrdinalIgnoreCase) == true) ||
            document.Descendants().Any(element =>
                element.Name.LocalName.Contains("Sort", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName.Contains("Spool", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName.Contains("Material", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName is "SpillOccurred" or "SpillWarning" or "SpillToTempDb") ||
            document.Descendants().SelectMany(element => element.Attributes()).Any(attribute =>
                attribute.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase) && IsPositive(attribute.Value)))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a SQL Server scan, sort, materialization, or spill.");

        var matches = relops.Where(element =>
                string.Equals(element.Attribute("PhysicalOp")?.Value, "Index Seek", StringComparison.Ordinal) &&
                element.Descendants().Any(objectElement => objectElement.Name.LocalName == "Object" &&
                    objectElement.Attribute("Table")?.Value.Trim('[', ']') == definition.TableName &&
                    objectElement.Attribute("Index")?.Value.Trim('[', ']') == physicalIndexName))
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained plan is not the exact SQL Server index seek.");
    }

    private static void ValidateMongoPlan(string plan, RouteDefinition definition, string physicalIndexName)
    {
        using var document = ParseJson(plan, "MongoDB");
        var stages = FindObjects(document.RootElement, "stage").ToArray();
        if (stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String &&
                                (value.GetString()?.Contains("SORT", StringComparison.OrdinalIgnoreCase) == true ||
                                 value.GetString()?.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) == true ||
                                 value.GetString()?.Contains("SPILL", StringComparison.OrdinalIgnoreCase) == true)) ||
            HasSpillMarker(document.RootElement))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a MongoDB sort, materialization, or spill.");
        if (stages.Any(stage => stage.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String &&
                                value.GetString()?.Contains("COLLSCAN", StringComparison.OrdinalIgnoreCase) == true))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained a MongoDB collection scan.");
        var matches = FindObjects(document.RootElement, "indexName")
            .Where(value => value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String &&
                            index.GetString() == physicalIndexName)
            .ToArray();
        if (matches.Length != 1 || !stages.Any(stage => stage.TryGetProperty("stage", out var value) &&
                                                        value.ValueKind == JsonValueKind.String && value.GetString() == "IXSCAN"))
            throw new PerformanceContractException(
                $"Schedule route '{definition.RouteIdentity}' retained plan is not the exact MongoDB index scan.");
    }

    private static JsonDocument ParseJson(string plan, string provider)
    {
        try
        {
            return JsonDocument.Parse(plan);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Schedule {provider} native plan is invalid JSON: {exception.Message}");
        }
    }

    private static IEnumerable<JsonElement> FindObjects(JsonElement value, string requiredProperty)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty(requiredProperty, out _))
                yield return value;
            foreach (var property in value.EnumerateObject())
                foreach (var match in FindObjects(property.Value, requiredProperty))
                    yield return match;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var match in FindObjects(item, requiredProperty))
                    yield return match;
        }
    }

    private static bool HasSpillMarker(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if ((property.Name.Contains("spill", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Contains("materializ", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("usedDisk", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("diskUse", StringComparison.OrdinalIgnoreCase)) &&
                    IsPositive(property.Value))
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

    private static bool IsPositive(JsonElement value) =>
        value.ValueKind == JsonValueKind.True ||
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count > 0 ||
        value.ValueKind == JsonValueKind.String &&
        (value.GetString() == "1" || string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));

    private static bool IsPositive(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0;

    private static Envelope Parse(string retained)
    {
        var commandMarker = "\n" + CommandSeparator + "\n";
        var planMarker = "\n" + PlanSeparator + "\n";
        var commandSplit = retained.IndexOf(commandMarker, StringComparison.Ordinal);
        var planSplit = commandSplit < 0 ? -1 : retained.IndexOf(planMarker, commandSplit + commandMarker.Length, StringComparison.Ordinal);
        if (commandSplit < 0 || planSplit < 0)
            throw new PerformanceContractException("Schedule retained native plan is missing its command or provider-plan envelope.");

        var header = retained[..commandSplit].Split('\n');
        if (header.Length != 12 || header[0] != Magic)
            throw new PerformanceContractException("Schedule retained native plan has an invalid evidence envelope header.");
        var command = retained[(commandSplit + commandMarker.Length)..planSplit];
        var plan = retained[(planSplit + planMarker.Length)..];
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(plan))
            throw new PerformanceContractException("Schedule retained native plan has an empty provider command or plan.");

        return new Envelope(
            Value(header[1], "provider"),
            Value(header[2], "workload"),
            Value(header[3], "route"),
            Value(header[4], "table"),
            Value(header[5], "index"),
            Value(header[6], "predicate-fields"),
            Value(header[7], "predicate-operators"),
            Value(header[8], "order"),
            PositiveInt(header[9], "physical-cardinality"),
            PositiveInt(header[10], "finite-limit"),
            PositiveInt(header[11], "materialized-candidates"),
            command,
            plan);
    }

    private static string Value(string line, string name)
    {
        var prefix = name + "=";
        return line.StartsWith(prefix, StringComparison.Ordinal) && line.Length > prefix.Length
            ? line[prefix.Length..]
            : throw new PerformanceContractException($"Schedule retained native plan is missing '{name}'.");
    }

    private static int PositiveInt(string line, string name) =>
        int.TryParse(Value(line, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new PerformanceContractException($"Schedule retained native plan has an invalid '{name}'.");

    private sealed record Envelope(
        string Provider,
        string WorkloadId,
        string RouteIdentity,
        string TableName,
        string IndexName,
        string PredicateFields,
        string PredicateOperators,
        string OrderFields,
        int PhysicalCardinality,
        int FiniteLimit,
        int MaterializedCandidateCount,
        string ProviderCommand,
        string ProviderPlan);
}
