using System.Text.RegularExpressions;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Classifies physical and provider-derived scans in SQLite EXPLAIN QUERY PLAN output.</summary>
public static class SqliteExplainPlanInspector
{
    private static readonly Regex Scan = new(
        @"\bSCAN\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DerivedScan = new(
        @"^SCAN\s+(?<alias>\(subquery-\d+\)|__groundwork_base|__(?:groundwork_total|groundwork_page))(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexedSearch = new(
        @"\bSEARCH\b.*\bUSING\s+(?:COVERING\s+)?INDEX\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> KnownGroundworkResults = new(StringComparer.OrdinalIgnoreCase)
    {
        "__groundwork_base",
        "__groundwork_total",
        "__groundwork_page"
    };

    /// <summary>
    /// Returns every SCAN which is not iteration over a closed Groundwork result alias or an
    /// optimizer-numbered subquery whose matching coroutine contains an indexed source search.
    /// </summary>
    public static IReadOnlyList<string> PhysicalScanLines(string plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var rawLines = plan.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nodes = rawLines.Select(Parse).ToArray();
        return nodes
            .Where(node => Scan.IsMatch(node.Detail) && !IsDerivedResultScan(node, nodes))
            .Select(node => node.Raw)
            .ToArray();
    }

    private static bool IsDerivedResultScan(PlanNode node, IReadOnlyList<PlanNode> nodes)
    {
        var match = DerivedScan.Match(node.Detail);
        if (!match.Success)
            return false;

        var alias = match.Groups["alias"].Value;
        if (KnownGroundworkResults.Contains(alias))
            return true;

        var producer = nodes.FirstOrDefault(candidate =>
            candidate.Id is not null &&
            string.Equals(candidate.Detail, "CO-ROUTINE " + alias, StringComparison.OrdinalIgnoreCase));
        return producer?.Id is { } producerId && nodes.Any(candidate =>
            IndexedSearch.IsMatch(candidate.Detail) && IsDescendantOf(candidate, producerId, nodes));
    }

    private static bool IsDescendantOf(PlanNode node, int ancestorId, IReadOnlyList<PlanNode> nodes)
    {
        var byId = nodes
            .Where(candidate => candidate.Id is not null)
            .GroupBy(candidate => candidate.Id!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var seen = new HashSet<int>();
        var parent = node.ParentId;
        while (parent is { } parentId && seen.Add(parentId))
        {
            if (parentId == ancestorId)
                return true;
            parent = byId.TryGetValue(parentId, out var parentNode) ? parentNode.ParentId : null;
        }

        return false;
    }

    private static PlanNode Parse(string raw)
    {
        var parts = raw.Split('\t', 4, StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], out var id) &&
            int.TryParse(parts[1], out var parentId))
        {
            return new PlanNode(raw, id, parentId, parts.Length == 4 ? parts[3] : parts[2]);
        }

        return new PlanNode(raw, null, null, raw);
    }

    private sealed record PlanNode(string Raw, int? Id, int? ParentId, string Detail);
}
