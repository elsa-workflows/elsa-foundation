using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Shrink-only ratchet on the provider-specific plan and command-text parsing that the benchmark
/// harness still carries (issue #1595). The parsers exist because Groundwork typed native-plan
/// evidence (valence-works/groundwork-v2#421, #422, #423) is not complete yet; #1594 retires them
/// route by route. Nothing may grow this footprint. When it shrinks, lower the baseline in the same
/// change so the ratchet keeps biting.
/// </summary>
public sealed class ProviderParsingFootprintRatchetTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string BaselinePath = Path.Combine(RepoRoot, "tests", "Elsa", "Architecture", "Baselines", "provider-parsing-footprint.json");

    /// <summary>Files whose whole purpose is parsing provider plans or command text.</summary>
    private static readonly string[] ParserFiles =
    [
        "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Harness/DiagnosticsNativePlan.cs",
        "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Harness/RuntimeNativePlan.cs",
        "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Harness/MongoExplainCommandInspector.cs",
        "benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/IamNativePlanParser.cs",
        "benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/DiagnosticsNativePlanCapture.cs",
        "benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/SecretNativePlanCapture.cs",
        "tools/groundwork/run-e3-medium-baseline.py"
    ];

    /// <summary>Provider syntax that only a parser of native plans or rendered commands needs.</summary>
    private static readonly (string Name, Regex Pattern)[] Markers =
    [
        ("showplan-xml", new Regex("ShowPlanXML|PhysicalOp=|<RelOp", RegexOptions.CultureInvariant)),
        ("explain-json", new Regex("\"Node Type\"|winningPlan|queryPlanner|executionStats", RegexOptions.CultureInvariant)),
        ("sqlite-explain", new Regex("USING INDEX|USING COVERING INDEX|TEMP B-TREE|SCAN ", RegexOptions.CultureInvariant)),
        ("sql-text", new Regex("COLLATE |DATALENGTH\\(|::text|OFFSET \\d|FETCH NEXT|string_agg", RegexOptions.CultureInvariant)),
        ("mongo-command", new Regex("\\$match|\\$sort|\\$limit|IXSCAN|COLLSCAN|SORT_MERGE", RegexOptions.CultureInvariant))
    ];

    private static readonly string[] ScanRoots = ["benchmarks", "tools/groundwork"];

    [Fact]
    public void Provider_parsing_footprint_matches_the_shrink_only_baseline()
    {
        var actual = Measure();
        var baseline = JsonSerializer.Deserialize<Footprint>(File.ReadAllText(BaselinePath), Options)
            ?? throw new InvalidOperationException("The provider-parsing footprint baseline is empty.");
        var failures = new List<string>();

        foreach (var (file, lines) in actual.ParserFileLines)
        {
            var allowed = baseline.ParserFileLines.GetValueOrDefault(file, 0);
            if (lines > allowed)
                failures.Add($"{file}: {lines} lines exceeds the baseline {allowed}. Provider-specific parsing may only shrink (#1594).");
        }
        foreach (var (marker, count) in actual.MarkerOccurrences)
        {
            var allowed = baseline.MarkerOccurrences.GetValueOrDefault(marker, 0);
            if (count > allowed)
                failures.Add($"marker '{marker}': {count} occurrences exceeds the baseline {allowed}. New provider syntax handling belongs in Groundwork typed evidence, not the harness.");
        }
        foreach (var (file, lines) in actual.ParserFileLines)
        {
            var allowed = baseline.ParserFileLines.GetValueOrDefault(file, 0);
            if (lines < allowed)
                failures.Add($"{file}: {lines} lines is below the baseline {allowed}. Lower the baseline in tests/Elsa/Architecture/Baselines/provider-parsing-footprint.json so the ratchet keeps biting.");
        }
        foreach (var (marker, count) in actual.MarkerOccurrences)
        {
            var allowed = baseline.MarkerOccurrences.GetValueOrDefault(marker, 0);
            if (count < allowed)
                failures.Add($"marker '{marker}': {count} occurrences is below the baseline {allowed}. Lower the baseline so the ratchet keeps biting.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Shipped_source_carries_no_provider_syntax()
    {
        // The product must speak Groundwork abstractions only; this is the boundary the harness ratchet
        // protects from the other side.
        var hits = new List<string>();
        foreach (var path in SourceFiles(Path.Combine(RepoRoot, "src")))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}OpenIddict{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue; // vendor host composition, admitted separately
            var text = File.ReadAllText(path);
            foreach (var (name, pattern) in Markers)
                if (pattern.IsMatch(text))
                    hits.Add($"{Path.GetRelativePath(RepoRoot, path)} matches '{name}'");
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    internal static Footprint Measure()
    {
        var files = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in ParserFiles)
        {
            var path = Path.Combine(RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            files[file] = File.Exists(path) ? File.ReadLines(path).Count() : 0;
        }
        var markers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, _) in Markers)
            markers[name] = 0;
        foreach (var root in ScanRoots)
        {
            foreach (var path in SourceFiles(Path.Combine(RepoRoot, root.Replace('/', Path.DirectorySeparatorChar))))
            {
                var text = File.ReadAllText(path);
                foreach (var (name, pattern) in Markers)
                    markers[name] += pattern.Matches(text).Count;
            }
        }
        return new Footprint(files, markers);
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path =>
                (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".py", StringComparison.Ordinal)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : [];

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal sealed record Footprint(
        [property: JsonPropertyName("parserFileLines")] SortedDictionary<string, int> ParserFileLines,
        [property: JsonPropertyName("markerOccurrences")] SortedDictionary<string, int> MarkerOccurrences);
}
