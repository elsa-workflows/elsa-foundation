using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Binds capture-observed cardinality and bounds to the retained provider plan itself. Admission reparses
/// this envelope and the native payload; route summary booleans and caller-supplied index names are never
/// accepted as proof.
/// </summary>
public static class SecretRetainedNativePlan
{
    private const string Magic = "GROUNDWORK-SECRET-NATIVE-PLAN/1";
    private const string Separator = "---provider-plan---";
    private static readonly Regex SqliteSearch = new(
        @"\bSEARCH\b[^\r\n]*\bUSING\s+(?:COVERING\s+)?INDEX\s+[\x22'`\[]?(?<index>[^\s\x22'`\]()]+)[\x22'`\]]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Create(
        string provider,
        int physicalCardinality,
        int finiteLimit,
        int materializedCandidateCount,
        string providerPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(physicalCardinality);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(finiteLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materializedCandidateCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPlan);
        return string.Join('\n',
            Magic,
            $"provider={provider}",
            $"physical-cardinality={physicalCardinality.ToString(CultureInfo.InvariantCulture)}",
            $"finite-limit={finiteLimit.ToString(CultureInfo.InvariantCulture)}",
            $"materialized-candidates={materializedCandidateCount.ToString(CultureInfo.InvariantCulture)}",
            Separator,
            providerPlan);
    }

    public static void Validate(string provider, string adapter, NativeRouteEvidence route, string retained)
    {
        var envelope = Parse(retained);
        if (!string.Equals(envelope.Provider, provider, StringComparison.Ordinal) ||
            envelope.PhysicalCardinality != route.PhysicalCardinality ||
            envelope.FiniteLimit != route.FiniteLimit ||
            envelope.MaterializedCandidateCount != route.MaterializedCandidateCount)
            throw new PerformanceContractException(
                "Secret retained native plan does not bind the route provider, physical cardinality, finite limit, and materialized count.");

        var proof = provider switch
        {
            "sqlite" => ParseSqlite(envelope.ProviderPlan, adapter),
            "postgresql" => ParsePostgreSql(envelope.ProviderPlan, adapter),
            "sqlserver" => ParseSqlServer(envelope.ProviderPlan, adapter),
            "mongodb" => ParseMongo(envelope.ProviderPlan),
            _ => throw new PerformanceContractException(
                $"Secret retained native-plan admission does not support provider '{provider}'.")
        };
        if (!string.Equals(route.PlanClassification, "index-search", StringComparison.Ordinal) ||
            !string.Equals(route.IndexName, proof.IndexName, StringComparison.Ordinal) ||
            !proof.HasStorageScopePredicate ||
            !proof.HasRoutePredicate ||
            proof.FiniteLimit is { } observedLimit && observedLimit != route.FiniteLimit ||
            !route.HasStorageScopePredicate ||
            !route.HasRoutePredicate)
            throw new PerformanceContractException(
                "Secret retained native plan does not structurally prove the admitted index-search and scope/route predicates.");
    }

    internal static string ProviderPlanForStructuredSafetyValidation(string content) =>
        content.StartsWith(Magic, StringComparison.Ordinal) ? Parse(content).ProviderPlan : content;

    private static Envelope Parse(string retained)
    {
        ArgumentNullException.ThrowIfNull(retained);
        var marker = "\n" + Separator + "\n";
        var split = retained.IndexOf(marker, StringComparison.Ordinal);
        if (split < 0)
            throw new PerformanceContractException("Secret retained native plan is missing its evidence envelope.");
        var header = retained[..split].Split('\n');
        if (header.Length != 5 || header[0] != Magic)
            throw new PerformanceContractException("Secret retained native plan has an invalid evidence envelope header.");
        return new Envelope(
            Value(header[1], "provider"),
            PositiveInt(header[2], "physical-cardinality"),
            PositiveInt(header[3], "finite-limit"),
            PositiveInt(header[4], "materialized-candidates"),
            retained[(split + marker.Length)..]);
    }

    private static string Value(string line, string name)
    {
        var prefix = name + "=";
        return line.StartsWith(prefix, StringComparison.Ordinal) && line.Length > prefix.Length
            ? line[prefix.Length..]
            : throw new PerformanceContractException($"Secret retained native plan is missing '{name}'.");
    }

    private static int PositiveInt(string line, string name) =>
        int.TryParse(Value(line, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new PerformanceContractException($"Secret retained native plan has an invalid '{name}'.");

    private static PlanProof ParseSqlite(string plan, string adapter)
    {
        var scan = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => ContainsToken(line, "SCAN") && !IsGroundworkMaterializedResultScan(line));
        if (scan is not null)
            throw new PerformanceContractException("Secret retained SQLite plan contains a physical SCAN.");
        var matches = SqliteSearch.Matches(plan)
            .Select(match => match.Groups["index"].Value.Trim('"', '`', '[', ']'))
            .Where(index => !index.Contains("SecretVersion", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException("Secret retained SQLite plan must contain exactly one route index SEARCH.");
        return new PlanProof(
            matches[0],
            IsEf(adapter)
                ? SqliteRequiresEquality(plan, "TenantId")
                : SqliteRequiresEquality(plan, "__groundwork_scope") && SqliteRequiresEquality(plan, "tenantId"),
            SqliteRequiresEquality(plan, "Status"));
    }

    private static PlanProof ParsePostgreSql(string plan, string adapter)
    {
        try
        {
            using var document = JsonDocument.Parse(plan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var predicates = new List<string>();
            var rejectedScan = false;
            VisitPostgreSql(document.RootElement, indexes, predicates, ref rejectedScan);
            if (rejectedScan || indexes.Count != 1)
                throw new PerformanceContractException("Secret retained PostgreSQL plan must prove one index scan and no physical table scan.");
            return new PlanProof(
                indexes.Single(),
                IsEf(adapter)
                    ? predicates.Any(predicate => RequiredEqualityParser.Requires(PredicateTokens(predicate), "TenantId"))
                    : predicates.Any(predicate => RequiredEqualityParser.Requires(PredicateTokens(predicate), "__groundwork_scope")) &&
                      predicates.Any(predicate => RequiredEqualityParser.Requires(PredicateTokens(predicate), "tenantId")),
                predicates.Any(predicate => RequiredEqualityParser.Requires(PredicateTokens(predicate), "status")));
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Secret retained PostgreSQL plan is invalid JSON: {exception.Message}");
        }
    }

    private static void VisitPostgreSql(
        JsonElement value,
        HashSet<string> indexes,
        List<string> predicates,
        ref bool rejectedScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("Node Type", out var node) && node.ValueKind == JsonValueKind.String)
            {
                var kind = node.GetString();
                if (kind is "Seq Scan" or "Parallel Seq Scan" or "Bitmap Heap Scan")
                    rejectedScan = true;
                if (kind is "Index Scan" or "Index Only Scan" &&
                    value.TryGetProperty("Index Name", out var index) && index.ValueKind == JsonValueKind.String)
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
            foreach (var item in value.EnumerateArray())
                VisitPostgreSql(item, indexes, predicates, ref rejectedScan);
    }

    private static PlanProof ParseSqlServer(string plan, string adapter)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(plan, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new PerformanceContractException($"Secret retained SQL Server plan is invalid XML: {exception.Message}");
        }
        if (document.Descendants().Where(element => element.Name.LocalName == "RelOp")
            .Any(element => ((string?)element.Attribute("PhysicalOp"))?.Contains("Scan", StringComparison.Ordinal) == true))
            throw new PerformanceContractException("Secret retained SQL Server plan contains a physical scan.");
        var indexes = document.Descendants()
            .Where(element => element.Name.LocalName == "RelOp" &&
                              string.Equals((string?)element.Attribute("PhysicalOp"), "Index Seek", StringComparison.Ordinal))
            .SelectMany(element => element.Descendants().Where(child => child.Name.LocalName == "Object"))
            .Select(element => ((string?)element.Attribute("Index"))?.Trim().Trim('[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (indexes.Length != 1)
            throw new PerformanceContractException("Secret retained SQL Server plan must contain exactly one Index Seek.");
        var predicateColumns = document.Descendants()
            .Where(element => element.Name.LocalName == "ColumnReference")
            .Select(element => ((string?)element.Attribute("Column"))?.Trim('[', ']'))
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PlanProof(
            indexes[0],
            IsEf(adapter)
                ? predicateColumns.Contains("TenantId") && SqlServerRequiresEquality(document, "TenantId")
                : predicateColumns.Contains("__groundwork_scope") && SqlServerRequiresEquality(document, "__groundwork_scope") &&
                  predicateColumns.Contains("tenantId") && SqlServerRequiresEquality(document, "tenantId"),
            predicateColumns.Contains("status") && SqlServerRequiresEquality(document, "status"));
    }

    private static PlanProof ParseMongo(string plan)
    {
        try
        {
            using var document = JsonDocument.Parse(plan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var collectionScan = false;
            VisitMongoPlans(document.RootElement, indexes, ref collectionScan);
            var commands = new List<JsonElement>();
            CollectMongoCommands(document.RootElement, commands);
            if (collectionScan || indexes.Count != 1 || commands.Count != 1)
                throw new PerformanceContractException("Secret retained MongoDB plan must prove one IXSCAN and one aggregate pipeline without COLLSCAN.");
            var pipeline = commands[0].GetProperty("pipeline");
            var matches = pipeline.EnumerateArray()
                .Where(stage => stage.ValueKind == JsonValueKind.Object &&
                                stage.TryGetProperty("$match", out var match) && match.ValueKind == JsonValueKind.Object)
                .Select(stage => stage.GetProperty("$match"))
                .ToArray();
            var limits = pipeline.EnumerateArray()
                .Where(stage => stage.ValueKind == JsonValueKind.Object && stage.TryGetProperty("$limit", out _))
                .Select(stage => stage.GetProperty("$limit"))
                .Where(value => value.TryGetInt32(out _))
                .Select(value => value.GetInt32())
                .ToArray();
            if (limits.Length != 1 || limits[0] <= 0)
                throw new PerformanceContractException("Secret retained MongoDB plan has no finite aggregate limit.");
            return new PlanProof(
                indexes.Single(),
                matches.Any(match => MongoRequiresEquality(match, "tenantId")),
                matches.Any(match => MongoRequiresEquality(match, "status")),
                limits[0]);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Secret retained MongoDB plan is invalid JSON: {exception.Message}");
        }
    }

    private static void VisitMongoPlans(JsonElement value, HashSet<string> indexes, ref bool collectionScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                if (property.Name == "winningPlan")
                    VisitMongoPlan(property.Value, indexes, ref collectionScan);
            foreach (var property in value.EnumerateObject())
                VisitMongoPlans(property.Value, indexes, ref collectionScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                VisitMongoPlans(item, indexes, ref collectionScan);
    }

    private static void VisitMongoPlan(JsonElement value, HashSet<string> indexes, ref bool collectionScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("stage", out var stage) && stage.ValueKind == JsonValueKind.String)
            {
                if (stage.GetString() == "COLLSCAN") collectionScan = true;
                if (stage.GetString() == "IXSCAN" && value.TryGetProperty("indexName", out var index) && index.ValueKind == JsonValueKind.String)
                    indexes.Add(index.GetString()!);
            }
            foreach (var property in value.EnumerateObject())
                VisitMongoPlan(property.Value, indexes, ref collectionScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                VisitMongoPlan(item, indexes, ref collectionScan);
    }

    private static void CollectMongoCommands(JsonElement value, List<JsonElement> commands)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.Object &&
                command.TryGetProperty("aggregate", out var aggregate) && aggregate.ValueKind == JsonValueKind.String &&
                command.TryGetProperty("pipeline", out var pipeline) && pipeline.ValueKind == JsonValueKind.Array)
                commands.Add(command.Clone());
            foreach (var property in value.EnumerateObject())
                CollectMongoCommands(property.Value, commands);
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                CollectMongoCommands(item, commands);
    }

    private static bool MongoRequiresEquality(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == field && IsMongoEquality(property.Value)) return true;
            if (property.Name == "$and" && property.Value.ValueKind == JsonValueKind.Array &&
                property.Value.EnumerateArray().Any(item => MongoRequiresEquality(item, field))) return true;
            if (property.Name == "$or" && property.Value.ValueKind == JsonValueKind.Array)
            {
                var branches = property.Value.EnumerateArray().ToArray();
                if (branches.Length != 0 && branches.All(item => MongoRequiresEquality(item, field))) return true;
            }
        }
        return false;
    }

    private static bool IsMongoEquality(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$eq", out var equality) &&
        equality.ValueKind is not JsonValueKind.Array and not JsonValueKind.Object;

    private static bool SqliteRequiresEquality(string plan, string field)
    {
        foreach (var line in plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ContainsToken(line, "SEARCH")) continue;
            var open = line.IndexOf('(');
            var close = line.LastIndexOf(')');
            if (open >= 0 && close > open &&
                RequiredEqualityParser.Requires(PredicateTokens(line[(open + 1)..close]), field, parameterOnly: true))
                return true;
        }
        return false;
    }

    private static bool SqlServerRequiresEquality(XDocument document, string field)
    {
        var seekEquality = document.Descendants()
            .Where(element => element.Name.LocalName is "Prefix" or "StartRange" or "EndRange" &&
                              string.Equals((string?)element.Attribute("ScanType"), "EQ", StringComparison.OrdinalIgnoreCase))
            .Any(element => element.Descendants().Any(column =>
                column.Name.LocalName == "ColumnReference" &&
                string.Equals(((string?)column.Attribute("Column"))?.Trim('[', ']'), field, StringComparison.OrdinalIgnoreCase)));
        if (seekEquality) return true;

        return document.Descendants()
            .Where(element => element.Name.LocalName == "Compare" &&
                              string.Equals((string?)element.Attribute("CompareOp"), "EQ", StringComparison.OrdinalIgnoreCase))
            .Any(compare => IsDirectSqlServerEquality(compare, field));
    }

    private static bool IsDirectSqlServerEquality(XElement compare, string field)
    {
        var operands = compare.Elements().Where(element => element.Name.LocalName == "ScalarOperator").ToArray();
        if (operands.Length != 2) return false;
        return IsDirectSqlServerColumn(operands[0], field) && IsSqlServerConstant(operands[1]) ||
               IsDirectSqlServerColumn(operands[1], field) && IsSqlServerConstant(operands[0]);
    }

    private static bool IsDirectSqlServerColumn(XElement operand, string field)
    {
        var columns = operand.Descendants().Where(element => element.Name.LocalName == "ColumnReference").ToArray();
        return columns.Length == 1 &&
               !operand.Descendants().Any(element => element.Name.LocalName is "IF" or "Intrinsic" or "Arithmetic" or "Compare") &&
               string.Equals(((string?)columns[0].Attribute("Column"))?.Trim('[', ']'), field, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqlServerConstant(XElement operand) =>
        operand.Descendants().Any(element => element.Name.LocalName is "Const" or "ParameterReference") &&
        !operand.Descendants().Any(element => element.Name.LocalName is "ColumnReference" or "IF" or "Intrinsic" or "Arithmetic" or "Compare");

    private static bool ContainsToken(string value, string token) =>
        PredicateTokens(value).Any(item => item.Kind == TokenKind.Identifier && item.Value.Equals(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsGroundworkMaterializedResultScan(string line) =>
        Regex.IsMatch(
            line,
            @"\bSCAN\s+__(?:groundwork_total|groundwork_page)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<Token> PredicateTokens(string value)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < value.Length;)
        {
            var current = value[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '-' && index + 1 < value.Length && value[index + 1] == '-')
            {
                index += 2;
                while (index < value.Length && value[index] is not '\r' and not '\n') index++;
                continue;
            }
            if (current == '/' && index + 1 < value.Length && value[index + 1] == '*')
            {
                var close = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? value.Length : close + 2;
                continue;
            }
            if (current == '\'')
            {
                index++;
                while (index < value.Length)
                {
                    if (value[index++] != '\'') continue;
                    if (index < value.Length && value[index] == '\'')
                    {
                        index++;
                        continue;
                    }
                    break;
                }
                tokens.Add(new Token(TokenKind.Value, "literal"));
                continue;
            }
            if (current is '"' or '`' or '[')
            {
                var close = current == '[' ? ']' : current;
                var start = ++index;
                while (index < value.Length && value[index] != close) index++;
                tokens.Add(new Token(TokenKind.Identifier, value[start..index]));
                if (index < value.Length) index++;
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                var start = index++;
                while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] == '_')) index++;
                tokens.Add(new Token(TokenKind.Identifier, value[start..index]));
                continue;
            }
            if (current is '?' or '@' or ':' or '$')
            {
                if (current == ':' && index + 1 < value.Length && value[index + 1] == ':')
                {
                    tokens.Add(new Token(TokenKind.Cast, "::"));
                    index += 2;
                    continue;
                }
                var start = index++;
                while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] == '_')) index++;
                tokens.Add(new Token(TokenKind.Parameter, value[start..index]));
                continue;
            }
            if (char.IsDigit(current))
            {
                var start = index++;
                while (index < value.Length && (char.IsDigit(value[index]) || value[index] == '.')) index++;
                tokens.Add(new Token(TokenKind.Value, value[start..index]));
                continue;
            }
            tokens.Add(new Token(current switch
            {
                '.' => TokenKind.Dot,
                '=' => TokenKind.Equals,
                '(' => TokenKind.LeftParenthesis,
                ')' => TokenKind.RightParenthesis,
                _ => TokenKind.Other
            }, current.ToString()));
            index++;
        }
        return tokens;
    }

    private static bool IsEf(string adapter) =>
        string.Equals(adapter, "ef-secret-repository", StringComparison.Ordinal);

    private sealed record Envelope(
        string Provider,
        int PhysicalCardinality,
        int FiniteLimit,
        int MaterializedCandidateCount,
        string ProviderPlan);
    private sealed record PlanProof(
        string IndexName,
        bool HasStorageScopePredicate,
        bool HasRoutePredicate,
        int? FiniteLimit = null);
    private sealed class RequiredEqualityParser
    {
        private readonly IReadOnlyList<Token> tokens;
        private readonly string field;
        private readonly bool parameterOnly;
        private int index;
        private bool valid = true;

        private RequiredEqualityParser(IReadOnlyList<Token> tokens, string field, bool parameterOnly)
        {
            this.tokens = tokens;
            this.field = field;
            this.parameterOnly = parameterOnly;
        }

        internal static bool Requires(IReadOnlyList<Token> tokens, string field, bool parameterOnly = false)
        {
            var parser = new RequiredEqualityParser(tokens, field, parameterOnly);
            var result = parser.ParseOr();
            return parser.valid && parser.index == tokens.Count && result;
        }

        private bool ParseOr()
        {
            var result = ParseAnd();
            while (Match("or")) result &= ParseAnd();
            return result;
        }

        private bool ParseAnd()
        {
            var result = ParseUnary();
            while (Match("and")) result |= ParseUnary();
            return result;
        }

        private bool ParseUnary()
        {
            if (Match("not"))
            {
                _ = ParseUnary();
                return false;
            }
            if (index < tokens.Count && tokens[index].Kind == TokenKind.LeftParenthesis)
            {
                index++;
                var result = ParseOr();
                if (index >= tokens.Count || tokens[index].Kind != TokenKind.RightParenthesis)
                {
                    valid = false;
                    return false;
                }
                index++;
                return result;
            }
            return ParseAtom();
        }

        private bool ParseAtom()
        {
            var start = index;
            var depth = 0;
            while (index < tokens.Count)
            {
                var token = tokens[index];
                if (depth == 0 && (token.Kind == TokenKind.RightParenthesis || token.Is("and") || token.Is("or"))) break;
                depth += token.Kind switch
                {
                    TokenKind.LeftParenthesis => 1,
                    TokenKind.RightParenthesis => -1,
                    _ => 0
                };
                if (depth < 0) break;
                index++;
            }
            if (start == index || depth != 0)
            {
                valid = false;
                return false;
            }
            return HasDirectEquality(tokens.Skip(start).Take(index - start).ToArray(), field, parameterOnly);
        }

        private bool Match(string keyword)
        {
            if (index >= tokens.Count || !tokens[index].Is(keyword)) return false;
            index++;
            return true;
        }
    }

    private static bool HasDirectEquality(IReadOnlyList<Token> atom, string field, bool parameterOnly)
    {
        for (var equals = 0; equals < atom.Count; equals++)
        {
            if (atom[equals].Kind != TokenKind.Equals) continue;
            if (IsDirectField(atom.Take(equals).ToArray(), field) && IsConstant(atom.Skip(equals + 1).ToArray(), parameterOnly) ||
                IsConstant(atom.Take(equals).ToArray(), parameterOnly) && IsDirectField(atom.Skip(equals + 1).ToArray(), field))
                return true;
        }
        return false;
    }

    private static bool IsDirectField(IReadOnlyList<Token> operand, string field)
    {
        if (operand.Count == 0 || operand[^1].Kind != TokenKind.Identifier ||
            !operand[^1].Value.Equals(field, StringComparison.OrdinalIgnoreCase)) return false;
        for (var index = operand.Count - 2; index >= 0; index--)
        {
            var expected = (operand.Count - 1 - index) % 2 == 1 ? TokenKind.Dot : TokenKind.Identifier;
            if (operand[index].Kind != expected) return false;
        }
        return true;
    }

    private static bool IsConstant(IReadOnlyList<Token> operand, bool parameterOnly)
    {
        if (operand.Count == 0 || operand[0].Kind is not (TokenKind.Parameter or TokenKind.Value) ||
            parameterOnly && operand[0].Kind != TokenKind.Parameter) return false;
        if (operand.Count == 1) return true;
        return !parameterOnly && operand.Count >= 3 && operand[1].Kind == TokenKind.Cast &&
               operand.Skip(2).Select(token => token.Kind).All(kind => kind is TokenKind.Identifier or TokenKind.Dot);
    }

    private enum TokenKind { Identifier, Parameter, Value, Dot, Cast, Equals, LeftParenthesis, RightParenthesis, Other }
    private readonly record struct Token(TokenKind Kind, string Value)
    {
        internal bool Is(string value) =>
            Kind == TokenKind.Identifier && Value.Equals(value, StringComparison.OrdinalIgnoreCase);
    }
}
