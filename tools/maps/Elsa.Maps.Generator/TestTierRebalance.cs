using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Elsa.Maps.Generator;

/// <summary>
/// Maintainer tool for the tier manifest (spec 263): reads the TRX files of one or more CI runs and reports each test
/// project's median wall time, the slowest classes of split projects, and a proposed PR-tier packing by longest-first bin
/// packing. <c>--write</c> updates only <c>medianMinutes</c>, with the median over every run given: one run is noisy
/// (a project's wall time varies by a factor of two between runs), so pass the TRX of several. The shard grouping stays a reviewed hand edit, so one noisy
/// run cannot reshuffle the shards. CI never runs it.
/// </summary>
public static class TestTierRebalance
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>Reports measured durations from <paramref name="trxDirectory"/>; with <paramref name="write"/>, records them.</summary>
    public static int Run(RepoContext repo, string trxDirectory, bool write)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var manifest = TestTierManifest.Read(repo);
        var byAssembly = manifest.Projects.ToDictionary(
            project => Path.GetFileNameWithoutExtension(project.Project), project => project.Project, StringComparer.OrdinalIgnoreCase);

        var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(trxDirectory, "*.trx", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(file);
            var definitions = document.Descendants(Trx + "UnitTest").ToDictionary(
                test => (string)test.Attribute("id")!,
                test => (Storage: Path.GetFileNameWithoutExtension((string?)test.Attribute("storage") ?? ""),
                         Class: (string?)test.Element(Trx + "TestMethod")?.Attribute("className") ?? ""));
            var results = document.Descendants(Trx + "UnitTestResult").ToArray();
            if (results.Length == 0 || !definitions.TryGetValue((string)results[0].Attribute("testId")!, out var first) ||
                !byAssembly.TryGetValue(first.Storage, out var project))
                continue;

            var times = document.Descendants(Trx + "Times").First();
            if (!samples.TryGetValue(project, out var list))
                samples[project] = list = [];
            list.Add((DateTimeOffset.Parse((string)times.Attribute("finish")!, CultureInfo.InvariantCulture) -
                      DateTimeOffset.Parse((string)times.Attribute("start")!, CultureInfo.InvariantCulture)).TotalMinutes);
            if (!manifest.Splits.ContainsKey(project))
                continue;
            if (!classes.TryGetValue(project, out var perClass))
                classes[project] = perClass = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var result in results)
                if (definitions.TryGetValue((string)result.Attribute("testId")!, out var definition))
                    perClass[definition.Class] = perClass.GetValueOrDefault(definition.Class) +
                        TimeSpan.Parse((string?)result.Attribute("duration") ?? "0:0:0", CultureInfo.InvariantCulture).TotalMinutes;
        }

        var wall = samples.ToDictionary(pair => pair.Key, pair => Median(pair.Value), StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"Measured {wall.Count} of {manifest.Projects.Count} manifest projects, " +
                          $"{(samples.Count == 0 ? 0 : samples.Values.Max(list => list.Count))} run(s) at most per project.");
        foreach (var (project, minutes) in wall.OrderByDescending(pair => pair.Value).Take(15))
            Console.WriteLine($"  {minutes,6:0.00} min  {project}");
        foreach (var (project, perClass) in classes)
        {
            Console.WriteLine($"Split {project}: slowest classes (sum of test durations over all runs given; a class runs serially):");
            foreach (var (name, minutes) in perClass.OrderByDescending(pair => pair.Value).Take(8))
                Console.WriteLine($"  {minutes,6:0.00} min  {name}");
        }

        ProposePrPacking(manifest, wall);

        if (!write)
            return 0;
        var path = repo.Absolute(TestTierManifest.Path);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var entry in root["projects"]!.AsArray().OfType<JsonObject>())
            if (wall.TryGetValue((string)entry["project"]!, out var minutes))
                entry["medianMinutes"] = Math.Round(minutes, 2);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
        Console.WriteLine($"Recorded measured minutes in {TestTierManifest.Path}; review the diff before committing.");
        return 0;
    }

    /// <summary>
    /// Prints a longest-first packing of the PR-tier units into as many bins as there are PR-only shards: a shard that
    /// also appears among the integration shards carries integration units until the switch (FR-011) and is not counted.
    /// </summary>
    private static void ProposePrPacking(TestTierManifest manifest, IReadOnlyDictionary<string, double> wall)
    {
        var integrationNames = manifest.Shards.Integration.Select(shard => shard.Name).ToHashSet(StringComparer.Ordinal);
        var bins = Math.Max(1, manifest.Shards.Pr.Count(shard => !integrationNames.Contains(shard.Name)));
        var loads = new double[bins];
        var counts = new int[bins];
        var units = manifest.Projects
            .Where(project => project.Tier == TestTierManifest.PrTier && !manifest.OwnJobs.ContainsKey(project.Project))
            .Select(project => wall.TryGetValue(project.Project, out var minutes) ? minutes : project.MedianMinutes ?? 0)
            .OrderByDescending(minutes => minutes);
        foreach (var minutes in units)
        {
            var target = Array.IndexOf(loads, loads.Min());
            loads[target] += minutes;
            counts[target]++;
        }
        Console.WriteLine($"Proposed PR packing into {bins} bin(s) (shard target {manifest.ShardTargetMinutes} min of test step):");
        for (var index = 0; index < bins; index++)
            Console.WriteLine($"  bin {index + 1}: {loads[index]:0.0} project-minutes, {counts[index]} projects");
        Console.WriteLine("Split projects count whole here; place their class groups by the class floors above.");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
