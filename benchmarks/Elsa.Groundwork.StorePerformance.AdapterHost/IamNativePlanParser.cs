using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Interprets the native explain artifact emitted by Groundwork.Diagnostics.
///
/// The provider session remains the authority for constructing and executing the explain probe. This
/// parser only turns that provider-owned artifact into the common route facts used by the benchmark
/// evidence document. In particular, it never treats the logical index declaration as the observed
/// physical index and it rejects a plan which does not prove an index search.
/// </summary>
internal static class IamNativePlanParser
{
    private static readonly Regex SqliteIndexSearch = new(
        @"\bSEARCH\b[^\r\n]*\bUSING\s+(?:COVERING\s+)?INDEX\s+[\x22'`\[]?(?<index>[^\s\x22'`\]()]+)[\x22'`\]]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SqliteScan = new(
        @"\bSCAN\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnsafeXmlName = new(
        "(?i)(password|pwd|credential|connection|string|secret|account[_-]?key|access[_-]?key|token|server|host|endpoint|data[_-]?source|database|initial[_-]?catalog|port|user[_-]?id|uid)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record ParsedPlan(string Format, string PhysicalIndexName, string PlanClassification, string Content);

    public static ParsedPlan Parse(string provider, string rawPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(rawPlan);

        return provider switch
        {
            "sqlite" => ParseSqlite(rawPlan),
            "postgresql" => ParsePostgreSql(rawPlan),
            "sqlserver" => ParseSqlServer(rawPlan),
            "mongodb" => ParseMongoDb(rawPlan),
            _ => throw new PerformanceContractException($"IAM native-plan parsing does not support provider '{provider}'.")
        };
    }

    /// <summary>
    /// Parses the Secret list route. A total-count page is deliberately one public repository query,
    /// but its provider plan can contain several branches (for example, one bounded page branch and
    /// one count branch) plus an include lookup for versions. The route table must expose one physical
    /// index across its branches; requiring one textual SEARCH would incorrectly reject that real shape.
    /// Every physical-table SCAN is rejected. SQLite may report iteration over a derived subquery or
    /// Groundwork's materialized base result as SCAN nodes around a windowed LatestPerKey route; those
    /// are not source-table access paths and are therefore not rejected here.
    /// </summary>
    public static ParsedPlan ParseSecret(string provider, string rawPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(rawPlan);
        if (!string.Equals(provider, "sqlite", StringComparison.Ordinal))
            return Parse(provider, rawPlan);

        var scanLines = rawPlan
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => SqliteScan.IsMatch(line) && !IsDerivedResultScan(line))
            .Take(3)
            .ToArray();
        if (scanLines.Length != 0)
            throw new PerformanceContractException(
                $"SQLite native plan contains a physical SCAN operation: {string.Join(" | ", scanLines)}");

        var matches = SqliteIndexSearch.Matches(rawPlan)
            // The public Secret route materializes versions through EF's split include. That
            // child lookup has its own primary-key index; it is not the list route's access path.
            // Retain only the route index while still parsing the exact observed SQL plan.
            .Where(match => !match.Groups["index"].Value.Contains("SecretVersion", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            throw new PerformanceContractException(
                "Secret SQLite native plan must contain an index SEARCH operation; scans are rejected.");
        var indexes = matches
            .Select(match => match.Groups["index"].Value.Trim('"', '`', '[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (indexes.Length != 1)
            throw new PerformanceContractException(
                "Secret SQLite native plan must use exactly one physical index across its bounded page and total-count branches.");
        return new ParsedPlan("sqlite-explain-query-plan", indexes[0], "index-search", rawPlan);
    }

    private static bool IsDerivedResultScan(string line) =>
        Regex.IsMatch(
            line,
            @"\bSCAN\s+(?:\(subquery-\d+\)|__groundwork_base|__(?:groundwork_total|groundwork_page))(?:\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Removes only connection-bearing metadata that a provider may include in its native artifact.
    /// The optimizer plan tree and selected index are retained byte-for-byte in meaning. SQLite and
    /// PostgreSQL explain outputs do not normally need redaction, but keeping this operation here makes
    /// the retained MongoDB/SQL Server plans safe for the benchmark artifact boundary as well.
    /// </summary>
    public static string NormalizeForArtifact(string provider, string rawPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(rawPlan);
        return provider switch
        {
            "mongodb" => NormalizeJson(rawPlan, removeUnsafeProperties: true),
            "sqlserver" => NormalizeXml(rawPlan),
            "sqlite" or "postgresql" => rawPlan,
            _ => throw new PerformanceContractException($"IAM native-plan normalization does not support provider '{provider}'.")
        };
    }

    internal static string RawPlanExtension(string provider) => provider switch
    {
        "sqlite" => ".txt",
        "postgresql" or "mongodb" => ".json",
        "sqlserver" => ".xml",
        _ => throw new PerformanceContractException($"IAM native-plan parsing does not support provider '{provider}'.")
    };

    private static ParsedPlan ParseSqlite(string rawPlan)
    {
        var matches = SqliteIndexSearch.Matches(rawPlan);
        if (matches.Count != 1)
            throw new PerformanceContractException(
                "IAM SQLite native plan must contain exactly one SEARCH ... USING INDEX operation; scans and ambiguous plans are rejected.");

        var index = matches[0].Groups["index"].Value.Trim('"', '`', '[', ']');
        if (string.IsNullOrWhiteSpace(index))
            throw new PerformanceContractException("IAM SQLite native plan did not expose a physical index name.");

        return new ParsedPlan("sqlite-explain-query-plan", index, "index-search", rawPlan);
    }

    private static ParsedPlan ParsePostgreSql(string rawPlan)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPlan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var sawRejectedScan = false;
            VisitPostgreSql(document.RootElement, indexes, ref sawRejectedScan);
            if (sawRejectedScan || indexes.Count != 1)
                throw new PerformanceContractException(
                    "IAM PostgreSQL native plan must prove exactly one Index Scan or Index Only Scan and no sequential scan.");

            return new ParsedPlan("postgresql-explain-json", indexes.Single(), "index-search", rawPlan);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"IAM PostgreSQL native plan is not valid explain JSON: {exception.Message}");
        }
    }

    private static void VisitPostgreSql(JsonElement value, HashSet<string> indexes, ref bool sawRejectedScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("Node Type", out var node) && node.ValueKind == JsonValueKind.String)
            {
                var nodeType = node.GetString() ?? string.Empty;
                if (nodeType is "Seq Scan" or "Parallel Seq Scan" or "Bitmap Heap Scan")
                    sawRejectedScan = true;
                if (nodeType is "Index Scan" or "Index Only Scan")
                {
                    if (!value.TryGetProperty("Index Name", out var index) || index.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(index.GetString()))
                        throw new PerformanceContractException("IAM PostgreSQL index plan did not expose its physical index name.");
                    indexes.Add(index.GetString()!);
                }
            }

            foreach (var property in value.EnumerateObject())
                VisitPostgreSql(property.Value, indexes, ref sawRejectedScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                VisitPostgreSql(item, indexes, ref sawRejectedScan);
        }
    }

    private static ParsedPlan ParseSqlServer(string rawPlan)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(rawPlan, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new PerformanceContractException($"IAM SQL Server native plan is not valid showplan XML: {exception.Message}");
        }

        var sawScan = document
            .Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Any(element => (string?)element.Attribute("PhysicalOp") is { } operation &&
                           operation.Contains("Scan", StringComparison.Ordinal));
        if (sawScan)
            throw new PerformanceContractException("IAM SQL Server native plan contains a scan; only an Index Seek is admissible.");

        var indexes = document
            .Descendants()
            .Where(element => element.Name.LocalName == "RelOp" &&
                              string.Equals((string?)element.Attribute("PhysicalOp"), "Index Seek", StringComparison.Ordinal))
            .SelectMany(element => element.Descendants().Where(descendant => descendant.Name.LocalName == "Object"))
            .Select(element => ((string?)element.Attribute("Index"))?.Trim().Trim('[', ']'))
            .Where(index => !string.IsNullOrWhiteSpace(index))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (indexes.Length != 1)
            throw new PerformanceContractException(
                "IAM SQL Server native plan must prove exactly one Index Seek with a physical index name.");

        return new ParsedPlan("sqlserver-statistics-xml", indexes[0], "index-search", rawPlan);
    }

    private static ParsedPlan ParseMongoDb(string rawPlan)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPlan);
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            var sawCollectionScan = false;
            VisitMongoWinningPlans(document.RootElement, indexes, ref sawCollectionScan);
            if (sawCollectionScan || indexes.Count != 1)
                throw new PerformanceContractException(
                    "IAM MongoDB native plan must prove exactly one winning IXSCAN and no winning COLLSCAN.");

            return new ParsedPlan("mongodb-explain-json", indexes.Single(), "index-search", rawPlan);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"IAM MongoDB native plan is not valid explain JSON: {exception.Message}");
        }
    }

    private static void VisitMongoWinningPlans(JsonElement value, HashSet<string> indexes, ref bool sawCollectionScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!string.Equals(property.Name, "winningPlan", StringComparison.Ordinal))
                    continue;

                VisitMongoPlan(property.Value, indexes, ref sawCollectionScan);
            }

            foreach (var property in value.EnumerateObject())
                VisitMongoWinningPlans(property.Value, indexes, ref sawCollectionScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                VisitMongoWinningPlans(item, indexes, ref sawCollectionScan);
        }
    }

    private static void VisitMongoPlan(JsonElement value, HashSet<string> indexes, ref bool sawCollectionScan)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("stage", out var stage) && stage.ValueKind == JsonValueKind.String)
            {
                switch (stage.GetString())
                {
                    case "COLLSCAN":
                        sawCollectionScan = true;
                        break;
                    case "IXSCAN":
                        if (!value.TryGetProperty("indexName", out var index) || index.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(index.GetString()))
                            throw new PerformanceContractException("IAM MongoDB winning IXSCAN did not expose its physical index name.");
                        indexes.Add(index.GetString()!);
                        break;
                }
            }

            foreach (var property in value.EnumerateObject())
                VisitMongoPlan(property.Value, indexes, ref sawCollectionScan);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                VisitMongoPlan(item, indexes, ref sawCollectionScan);
        }
    }

    private static string NormalizeJson(string rawPlan, bool removeUnsafeProperties)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPlan);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                WriteJson(writer, document.RootElement, removeUnsafeProperties);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"IAM native plan is not valid JSON: {exception.Message}");
        }
    }

    private static void WriteJson(Utf8JsonWriter writer, JsonElement value, bool removeUnsafeProperties)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    if (removeUnsafeProperties && IsUnsafeMetadataName(property.Name))
                        continue;
                    writer.WritePropertyName(property.Name);
                    WriteJson(writer, property.Value, removeUnsafeProperties);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteJson(writer, item, removeUnsafeProperties);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string NormalizeXml(string rawPlan)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(rawPlan, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new PerformanceContractException($"IAM SQL Server native plan is not valid showplan XML: {exception.Message}");
        }

        // Rebuild the tree with local element names. Merely removing the xmlns attribute is not enough:
        // LINQ to XML would otherwise re-introduce the namespace declaration when serializing the
        // namespaced element. Namespace URLs and connection-bearing metadata are not optimizer facts.
        var root = NormalizeXmlElement(document.Root)
                   ?? throw new PerformanceContractException("IAM SQL Server native plan did not contain a plan root element.");
        return new XDocument(root).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? NormalizeXmlElement(XElement? source)
    {
        if (source is null || UnsafeXmlName.IsMatch(source.Name.LocalName))
            return null;

        var target = new XElement(source.Name.LocalName,
            source.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration && !UnsafeXmlName.IsMatch(attribute.Name.LocalName))
                .Select(attribute => new XAttribute(attribute.Name.LocalName, attribute.Value)));
        foreach (var node in source.Nodes())
        {
            if (node is XElement child)
            {
                var normalized = NormalizeXmlElement(child);
                if (normalized is not null)
                    target.Add(normalized);
            }
            else if (node is XText text)
                target.Add(new XText(text.Value));
        }

        return target;
    }

    private static bool IsUnsafeMetadataName(string name) => UnsafeXmlName.IsMatch(name);
}
