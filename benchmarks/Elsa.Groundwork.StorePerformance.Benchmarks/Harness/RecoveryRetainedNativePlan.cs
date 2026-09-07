using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Retains the provider command and native plan for one recovery route in a fail-closed envelope.
/// Admission derives the route contract from this assembly; values copied into the evidence summary
/// or envelope are only accepted after they agree with the frozen route and the parsed provider shape.
/// </summary>
public static class RecoveryRetainedNativePlan
{
    private const string Magic = "GROUNDWORK-RECOVERY-NATIVE-PLAN/1";
    private const string CommandSeparator = "---provider-command---";
    private const string PlanSeparator = "---provider-plan---";
    private static readonly Regex SqliteSearch = new(
        @"\bSEARCH\b[^\r\n]*\bUSING\s+(?:COVERING\s+)?INDEX\s+[\x22'`\[]?(?<index>[^\s\x22'`()]+)[\x22'`\]]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record RouteDefinition(
        string RouteIdentity,
        string IndexName,
        string PredicateField,
        string PredicateOperator,
        IReadOnlyList<string> OrderFields,
        int PhysicalCardinality,
        int FiniteLimit,
        int MaterializedCandidateCount);

    public static RouteDefinition Definition(string routeIdentity) => routeIdentity switch
    {
        "list-recovery-detected" => New(routeIdentity, "by_recovery_detected", "interruptedExecutionStatus", "=", "interruptedExecutionAt"),
        "list-recovery-by-lease-expiry" => New(routeIdentity, "by_recovery_lease_expiry", "executionLeaseExpiresAt", "<=", "executionLeaseExpiresAt"),
        "list-recovery-by-lease-acquisition" => New(routeIdentity, "by_recovery_lease_acquisition", "executionLeaseAcquiredAt", "<=", "executionLeaseAcquiredAt"),
        "list-recovery-by-heartbeat" => New(routeIdentity, "by_recovery_heartbeat", "heartbeatRecordedAt", "<=", "heartbeatRecordedAt"),
        _ => throw new PerformanceContractException($"Recovery native-plan admission does not recognize route '{routeIdentity}'.")
    };

    public static string Create(string provider, string routeIdentity, string providerCommand, string providerPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPlan);
        var definition = Definition(routeIdentity);
        return string.Join('\n',
            Magic,
            $"provider={provider}",
            $"route={definition.RouteIdentity}",
            $"index={definition.IndexName}",
            $"predicate={definition.PredicateField}",
            $"predicate-operator={definition.PredicateOperator}",
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
        var envelope = Parse(retained);
        var definition = Definition(route.RouteIdentity);
        if (!string.Equals(envelope.Provider, provider, StringComparison.Ordinal) ||
            !string.Equals(envelope.RouteIdentity, definition.RouteIdentity, StringComparison.Ordinal) ||
            !string.Equals(envelope.IndexName, definition.IndexName, StringComparison.Ordinal) ||
            !string.Equals(envelope.PredicateField, definition.PredicateField, StringComparison.Ordinal) ||
            !string.Equals(envelope.PredicateOperator, definition.PredicateOperator, StringComparison.Ordinal) ||
            !string.Equals(envelope.OrderFields, string.Join(',', definition.OrderFields), StringComparison.Ordinal) ||
            envelope.PhysicalCardinality != definition.PhysicalCardinality ||
            envelope.FiniteLimit != definition.FiniteLimit ||
            envelope.MaterializedCandidateCount != definition.MaterializedCandidateCount ||
            route.PhysicalCardinality != definition.PhysicalCardinality ||
            route.FiniteLimit != definition.FiniteLimit ||
            route.MaterializedCandidateCount != definition.MaterializedCandidateCount ||
            route.PlanClassification != "index-search" ||
            !string.Equals(route.IndexName, definition.IndexName, StringComparison.Ordinal) ||
            !route.HasStorageScopePredicate ||
            !route.HasRoutePredicate)
            throw new PerformanceContractException(
                "Recovery retained native plan does not bind the frozen route, index, predicate, order, cardinality, finite limit, and materialized count.");

        ValidateCommand(envelope.ProviderCommand, definition, provider);
        switch (provider)
        {
            case "sqlite":
                ValidateSqlite(envelope.ProviderPlan, definition);
                break;
            case "postgresql":
                ValidatePostgreSql(envelope.ProviderPlan, definition);
                break;
            case "sqlserver":
                ValidateSqlServer(envelope.ProviderPlan, definition);
                break;
            case "mongodb":
                ValidateMongo(envelope.ProviderPlan, definition);
                break;
            default:
                throw new PerformanceContractException($"Recovery retained native-plan admission does not support provider '{provider}'.");
        }
    }

    internal static string ProviderPlanForStructuredSafetyValidation(string content) =>
        content.StartsWith(Magic, StringComparison.Ordinal) ? Parse(content).ProviderPlan : content;

    private static RouteDefinition New(string route, string index, string predicate, string predicateOperator, string firstOrder) =>
        new(route, index, predicate, predicateOperator, [firstOrder, "workflowExecutionId", "operationalStateId"], 2048, 1, 1);

    private static Envelope Parse(string retained)
    {
        ArgumentNullException.ThrowIfNull(retained);
        var commandMarker = "\n" + CommandSeparator + "\n";
        var planMarker = "\n" + PlanSeparator + "\n";
        var commandSplit = retained.IndexOf(commandMarker, StringComparison.Ordinal);
        if (commandSplit < 0)
            throw new PerformanceContractException("Recovery retained native plan is missing its command envelope.");
        var planSplit = retained.IndexOf(planMarker, commandSplit + commandMarker.Length, StringComparison.Ordinal);
        if (planSplit < 0)
            throw new PerformanceContractException("Recovery retained native plan is missing its provider-plan envelope.");

        var header = retained[..commandSplit].Split('\n');
        if (header.Length != 10 || header[0] != Magic)
            throw new PerformanceContractException("Recovery retained native plan has an invalid evidence envelope header.");
        var command = retained[(commandSplit + commandMarker.Length)..planSplit];
        var plan = retained[(planSplit + planMarker.Length)..];
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(plan))
            throw new PerformanceContractException("Recovery retained native plan has an empty provider command or plan.");
        return new Envelope(
            Value(header[1], "provider"),
            Value(header[2], "route"),
            Value(header[3], "index"),
            Value(header[4], "predicate"),
            Value(header[5], "predicate-operator"),
            Value(header[6], "order"),
            PositiveInt(header[7], "physical-cardinality"),
            PositiveInt(header[8], "finite-limit"),
            PositiveInt(header[9], "materialized-candidates"),
            command,
            plan);
    }

    private static string Value(string line, string name)
    {
        var prefix = name + "=";
        return line.StartsWith(prefix, StringComparison.Ordinal) && line.Length > prefix.Length
            ? line[prefix.Length..]
            : throw new PerformanceContractException($"Recovery retained native plan is missing '{name}'.");
    }

    private static int PositiveInt(string line, string name) =>
        int.TryParse(Value(line, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new PerformanceContractException($"Recovery retained native plan has an invalid '{name}'.");

    private static void ValidateCommand(string command, RouteDefinition definition, string provider)
    {
        var hasOrder = ContainsToken(command, "ORDER") || ContainsToken(command, "sort");
        if (!ContainsToken(command, "__groundwork_scope") ||
            !ContainsToken(command, "runtime_execution_liveness_state") ||
            !ContainsToken(command, definition.PredicateField) ||
            !HasExpectedCommandPredicate(command, definition, provider) ||
            !hasOrder ||
            !ContainsToken(command, "LIMIT") && !ContainsToken(command, "FETCH") && !ContainsToken(command, "TOP") ||
            command.IndexOf(definition.OrderFields[0], StringComparison.OrdinalIgnoreCase) < 0 ||
            command.IndexOf(definition.OrderFields[1], StringComparison.OrdinalIgnoreCase) < 0 ||
            command.IndexOf(definition.OrderFields[2], StringComparison.OrdinalIgnoreCase) < 0 ||
            !AppearsInOrder(command, definition.OrderFields) ||
            HasWrongLiteralLimit(command, definition.FiniteLimit))
            throw new PerformanceContractException(
                "Recovery retained native command does not prove the scoped route predicate, due ordering, and finite page bound.");
    }

    private static bool AppearsInOrder(string value, IReadOnlyList<string> fields)
    {
        var start = value.IndexOf("ORDER", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            start = value.IndexOf("sort", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        var previous = start;
        foreach (var field in fields)
        {
            var next = value.IndexOf(field, previous, StringComparison.OrdinalIgnoreCase);
            if (next < 0) return false;
            previous = next + field.Length;
        }
        var orderEnd = value.Length;
        foreach (var marker in new[] { "LIMIT", "FETCH", "TOP" })
        {
            var index = value.IndexOf(marker, start + 1, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) orderEnd = Math.Min(orderEnd, index);
        }
        return !Regex.IsMatch(value[start..orderEnd], @"\bDESC(?:ENDING)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasExpectedCommandPredicate(string command, RouteDefinition definition, string provider)
    {
        if (provider == "mongodb")
            return definition.PredicateOperator == "="
                ? command.Contains("$eq", StringComparison.OrdinalIgnoreCase)
                : command.Contains("$lte", StringComparison.OrdinalIgnoreCase);

        var position = command.IndexOf(definition.PredicateField, StringComparison.OrdinalIgnoreCase);
        if (position < 0) return false;
        var tail = command[(position + definition.PredicateField.Length)..];
        return definition.PredicateOperator == "="
            ? tail.TrimStart('"', '`', ']', ' ', '\t').StartsWith('=')
            : tail.TrimStart('"', '`', ']', ' ', '\t').StartsWith("<=", StringComparison.Ordinal);
    }

    private static bool HasExpectedPlanPredicate(string plan, RouteDefinition definition, bool allowSqliteNormalizedLessThan = false)
    {
        var position = plan.IndexOf(definition.PredicateField, StringComparison.OrdinalIgnoreCase);
        if (position < 0) return false;
        var tail = plan[(position + definition.PredicateField.Length)..].TrimStart('"', '`', ']', ' ', '\t');
        return definition.PredicateOperator == "="
            ? tail.StartsWith('=')
            : tail.StartsWith("<=", StringComparison.Ordinal) ||
              allowSqliteNormalizedLessThan && tail.StartsWith('<');
    }

    private static bool HasWrongLiteralLimit(string command, int expectedLimit)
    {
        foreach (Match match in Regex.Matches(
                     command,
                     "(?:\\bLIMIT\\b|\\bFETCH\\s+FIRST\\b|\\bFETCH\\s+NEXT\\b|\\bTOP\\b|[\"']limit[\"'])\\s*[(:= ]\\s*(?<value>\\d+)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!int.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value != expectedLimit)
                return true;
        }
        return false;
    }

    private static void ValidateSqlite(string plan, RouteDefinition definition)
    {
        var lines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Any(line => ContainsToken(line, "SCAN") && !IsMaterializedResultScan(line)))
            throw new PerformanceContractException("Recovery retained SQLite plan contains a physical scan.");
        var matches = SqliteSearch.Matches(plan)
            .Select(match => match.Groups["index"].Value.Trim('"', '`', '[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1 || !string.Equals(matches[0], definition.IndexName, StringComparison.Ordinal) ||
            !ContainsToken(plan, definition.PredicateField) ||
            !HasExpectedPlanPredicate(plan, definition, allowSqliteNormalizedLessThan: true))
            throw new PerformanceContractException("Recovery retained SQLite plan does not prove the exact route index and predicate.");
    }

    private static void ValidatePostgreSql(string plan, RouteDefinition definition)
    {
        try
        {
            using var document = JsonDocument.Parse(plan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var predicates = new List<string>();
            var rejectedScan = false;
            VisitPostgreSql(document.RootElement, indexes, predicates, ref rejectedScan);
            if (rejectedScan || indexes.Count != 1 || !indexes.Contains(definition.IndexName) ||
                !predicates.Any(predicate => ContainsToken(predicate, definition.PredicateField) && HasExpectedPlanPredicate(predicate, definition)))
                throw new PerformanceContractException("Recovery retained PostgreSQL plan does not prove the exact route index and predicate without a scan.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Recovery retained PostgreSQL plan is invalid JSON: {exception.Message}");
        }
    }

    private static void VisitPostgreSql(JsonElement value, HashSet<string> indexes, List<string> predicates, ref bool rejectedScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("Node Type", out var node) && node.ValueKind == JsonValueKind.String)
            {
                var kind = node.GetString();
                if (kind is "Seq Scan" or "Parallel Seq Scan" or "Bitmap Heap Scan") rejectedScan = true;
                if (kind is "Index Scan" or "Index Only Scan" && value.TryGetProperty("Index Name", out var index) && index.ValueKind == JsonValueKind.String)
                    indexes.Add(index.GetString()!);
            }
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "Index Cond" or "Filter" && property.Value.ValueKind == JsonValueKind.String)
                    predicates.Add(property.Value.GetString()!);
                VisitPostgreSql(property.Value, indexes, predicates, ref rejectedScan);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) VisitPostgreSql(item, indexes, predicates, ref rejectedScan);
    }

    private static void ValidateSqlServer(string plan, RouteDefinition definition)
    {
        XDocument document;
        try { document = XDocument.Parse(plan, LoadOptions.PreserveWhitespace); }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        { throw new PerformanceContractException($"Recovery retained SQL Server plan is invalid XML: {exception.Message}"); }

        if (document.Descendants().Where(element => element.Name.LocalName == "RelOp")
                .Any(element => ((string?)element.Attribute("PhysicalOp"))?.Contains("Scan", StringComparison.Ordinal) == true))
            throw new PerformanceContractException("Recovery retained SQL Server plan contains a physical scan.");
        var indexes = document.Descendants()
            .Where(element => element.Name.LocalName == "RelOp" && string.Equals((string?)element.Attribute("PhysicalOp"), "Index Seek", StringComparison.Ordinal))
            .SelectMany(element => element.Descendants().Where(child => child.Name.LocalName == "Object"))
            .Select(element => ((string?)element.Attribute("Index"))?.Trim().Trim('[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var columns = document.Descendants()
            .Where(element => element.Name.LocalName == "ColumnReference")
            .Select(element => ((string?)element.Attribute("Column"))?.Trim('[', ']'))
            .Where(column => !string.IsNullOrWhiteSpace(column)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (indexes.Length != 1 || !string.Equals(indexes[0], definition.IndexName, StringComparison.Ordinal) ||
            !columns.Contains(definition.PredicateField))
            throw new PerformanceContractException("Recovery retained SQL Server plan does not prove the exact route index and predicate.");
    }

    private static void ValidateMongo(string plan, RouteDefinition definition)
    {
        try
        {
            using var document = JsonDocument.Parse(plan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var collectionScan = false;
            VisitMongoPlans(document.RootElement, indexes, ref collectionScan);
            if (collectionScan || indexes.Count != 1 || !indexes.Contains(definition.IndexName))
                throw new PerformanceContractException("Recovery retained MongoDB plan does not prove one exact IXSCAN without a COLLSCAN.");
            if (!ContainsJsonField(document.RootElement, definition.PredicateField))
                throw new PerformanceContractException("Recovery retained MongoDB plan does not prove the exact route predicate.");
        }
        catch (JsonException exception)
        { throw new PerformanceContractException($"Recovery retained MongoDB plan is invalid JSON: {exception.Message}"); }
    }

    private static void VisitMongoPlans(JsonElement value, HashSet<string> indexes, ref bool collectionScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("stage", out var stage) && stage.ValueKind == JsonValueKind.String)
            {
                if (stage.GetString() == "COLLSCAN") collectionScan = true;
                if (stage.GetString() == "IXSCAN" && value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String)
                    indexes.Add(index.GetString()!);
            }
            foreach (var property in value.EnumerateObject()) VisitMongoPlans(property.Value, indexes, ref collectionScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) VisitMongoPlans(item, indexes, ref collectionScan);
    }

    private static bool ContainsJsonField(JsonElement value, string field)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.Equals(field, StringComparison.OrdinalIgnoreCase)) return true;
                if (ContainsJsonField(property.Value, field)) return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(item => ContainsJsonField(item, field));
        return false;
    }

    private static bool ContainsToken(string value, string token) =>
        Regex.IsMatch(value, $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsMaterializedResultScan(string line) =>
        Regex.IsMatch(line, @"\bSCAN\s+__(?:groundwork_total|groundwork_page)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Envelope(
        string Provider,
        string RouteIdentity,
        string IndexName,
        string PredicateField,
        string PredicateOperator,
        string OrderFields,
        int PhysicalCardinality,
        int FiniteLimit,
        int MaterializedCandidateCount,
        string ProviderCommand,
        string ProviderPlan);
}
