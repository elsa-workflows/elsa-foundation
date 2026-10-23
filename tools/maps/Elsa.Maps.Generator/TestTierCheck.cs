using System.Globalization;
using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>
/// Validates the test tier manifest against the tree (spec 263, FR-015 to FR-018). Every test project is in the
/// manifest exactly once. A project that reaches Testcontainers, or that launches a child host and takes two minutes or
/// more, is integration tier. Split classes exist and no shared xUnit collection is split. Every unit runs in exactly one
/// shard of its tier. CI runs it in "Generated maps fresh".
/// </summary>
public static partial class TestTierCheck
{
    private const string IntegrationFilterPath = "Elsa.Server.Persistence.Integration.slnf";
    private const double HostLaunchMinutes = 2;
    private const string Tiers = "tiers: pr, integration";

    /// <summary>Validates the committed manifest; prints every problem and returns 1 when there is one.</summary>
    public static int Run(RepoContext repo)
    {
        var manifest = TestTierManifest.Read(repo);
        var problems = Validate(manifest, Gather(repo, manifest));
        if (problems.Count == 0)
        {
            Console.WriteLine($"Test tiers: {manifest.Projects.Count} test projects, each in one tier and one shard.");
            return 0;
        }

        Console.Error.WriteLine($"{TestTierManifest.Path} does not match the tree ({problems.Count} problem(s)):");
        foreach (var problem in problems)
            Console.Error.WriteLine($"  - {problem}");
        Console.Error.WriteLine("See docs/contributing/merging.md#ci-tiers for the tier rule.");
        return 1;
    }

    /// <summary>Reads the facts the check needs from the working tree.</summary>
    public static TestTierFacts Gather(RepoContext repo, TestTierManifest manifest)
    {
        var projectFiles = repo.ListFiles(RepoLayout.ProjectPathspecs).Where(RepoLayout.IsTestProjectPath).ToArray();
        var testProjects = projectFiles
            .Where(path => TestSdkReference().IsMatch(File.ReadAllText(repo.Absolute(path))))
            .ToArray();
        var sources = SourcesByProject(repo, projectFiles);
        var references = SolutionFilterGenerator.GetProjectReferences(repo);

        var hostLaunchers = testProjects
            .Where(project => Closure(project, references).Any(owner =>
                sources.TryGetValue(owner, out var files) && files.Any(file => LaunchesDotnet(File.ReadAllText(repo.Absolute(file))))))
            .ToArray();

        var classes = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in manifest.Splits.Keys)
            classes[project] = ReadClasses(sources.TryGetValue(project, out var files)
                ? files.Select(file => File.ReadAllText(repo.Absolute(file))).ToArray()
                : []);

        return new TestTierFacts(testProjects, SolutionFilterGenerator.GetRoots(repo, IntegrationFilterPath), hostLaunchers, classes);
    }

    /// <summary>Every problem with the manifest, given the facts; empty when it matches.</summary>
    public static IReadOnlyList<string> Validate(TestTierManifest manifest, TestTierFacts facts)
    {
        var problems = new List<string>();
        var testProjects = facts.TestProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var containers = facts.TestcontainersRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exempt = manifest.HostLaunchExemptions.Select(entry => entry.Project).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var launchers = facts.HostLaunchers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, TestTierProject>(StringComparer.OrdinalIgnoreCase);

        // FR-016: every test project once, no entry for anything else.
        foreach (var entry in manifest.Projects)
        {
            if (!entries.TryAdd(entry.Project, entry))
                problems.Add($"{entry.Project} is listed more than once. Keep one entry ({Tiers}).");
            else if (!testProjects.Contains(entry.Project))
                problems.Add($"{entry.Project} is listed but is not a test project of the tree. Remove the entry ({Tiers}).");
            if (entry.Tier is not (TestTierManifest.PrTier or TestTierManifest.IntegrationTier))
                problems.Add($"{entry.Project} has tier \"{entry.Tier}\". Use one of the two ({Tiers}).");
        }
        foreach (var project in testProjects.Where(project => !entries.ContainsKey(project)).Order(StringComparer.Ordinal))
            problems.Add($"{project} is a test project with no entry in {TestTierManifest.Path}. Add it to one of the two tiers ({Tiers}): integration if it reaches Testcontainers or launches a child host and takes 2 minutes or more, otherwise pr.");

        // FR-017: the hard traits.
        foreach (var entry in entries.Values)
        {
            var hasContainers = containers.Contains(entry.Project);
            var launchesHost = launchers.Contains(entry.Project) && !exempt.Contains(entry.Project);
            var detected = hasContainers ? "testcontainers" : launchesHost ? "child-process" : null;
            if (!string.Equals(entry.Trait, detected, StringComparison.Ordinal))
                problems.Add($"{entry.Project} records trait \"{entry.Trait ?? "none"}\" but the tree shows \"{detected ?? "none"}\". Correct the entry ({Tiers}).");
            if (entry.Tier == TestTierManifest.PrTier && hasContainers)
                problems.Add($"{entry.Project} references Testcontainers, directly or through a referenced test project, so it belongs in the integration tier ({Tiers}).");
            if (entry.Tier == TestTierManifest.PrTier && launchesHost && (entry.MedianMinutes ?? 0) >= HostLaunchMinutes)
                problems.Add($"{entry.Project} launches a child host and takes {entry.MedianMinutes?.ToString("0.##", CultureInfo.InvariantCulture)} minutes, so it belongs in the integration tier ({Tiers}).");
        }
        foreach (var exemption in manifest.HostLaunchExemptions)
        {
            if (string.IsNullOrWhiteSpace(exemption.Reason))
                problems.Add($"Host-launch exemption for {exemption.Project} has no reason.");
            if (!launchers.Contains(exemption.Project))
                problems.Add($"Host-launch exemption for {exemption.Project} is stale: the project no longer launches dotnet. Remove it.");
        }
        foreach (var (project, job) in manifest.OwnJobs)
            if (!entries.TryGetValue(project, out var entry) || entry.Tier != TestTierManifest.PrTier)
                problems.Add($"{project} runs in its own job \"{job}\" and must be a pr-tier entry.");

        // Splits: one rest group, named classes exist, no collection split.
        foreach (var (project, split) in manifest.Splits)
        {
            if (split.Groups.Count(group => group.Rest) != 1)
                problems.Add($"Split {project} needs exactly one rest group, so a new class always runs somewhere.");
            if (split.Groups.Select(group => group.Name).Distinct(StringComparer.Ordinal).Count() != split.Groups.Count)
                problems.Add($"Split {project} repeats a group name.");
            var classes = facts.ClassesByProject.TryGetValue(project, out var found) ? found : new Dictionary<string, string?>();
            var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in split.Groups.Where(group => !group.Rest))
                foreach (var name in group.Classes)
                {
                    if (!classes.ContainsKey(name))
                        problems.Add($"Split {project} group {group.Name} names {name}, which is not a class of the project.");
                    if (!groupOf.TryAdd(name, group.Name))
                        problems.Add($"Split {project} names {name} in more than one group.");
                }
            var restName = split.Groups.FirstOrDefault(group => group.Rest)?.Name ?? "rest";
            foreach (var collection in classes.Where(pair => pair.Value is not null).GroupBy(pair => pair.Value, StringComparer.Ordinal))
            {
                var groups = collection.Select(pair => groupOf.GetValueOrDefault(pair.Key, restName)).Distinct(StringComparer.Ordinal).ToArray();
                if (groups.Length > 1)
                    problems.Add($"Split {project} puts the classes of xUnit collection \"{collection.Key}\" in groups {string.Join(", ", groups)}; a collection must stay in one group.");
            }
        }

        // Shards: every unit of a tier exactly once.
        var shardUnits = UnitsOf(manifest, entries, containers);
        CheckShards(problems, "pr", manifest.Shards.Pr, manifest.IntegrationLeavesPrTier
            ? shardUnits.Pr
            : shardUnits.Pr.Concat(shardUnits.Integration).ToArray());
        CheckShards(problems, "integration", manifest.Shards.Integration, shardUnits.Integration);

        var suites = manifest.ContainerSuites.Select(suite => suite.Suite)
            .Concat(manifest.SecretsCompositionSuite is { } secrets ? [secrets.Suite] : [])
            .ToArray();
        CheckCoverage(problems, "containers", manifest.Shards.Containers.SelectMany(shard => shard.Suites).ToArray(), suites);

        foreach (var entry in manifest.Quarantine)
            if (!TestName().IsMatch(entry.Test) || entry.Issue <= 0)
                problems.Add($"Quarantine entry \"{entry.Test}\" needs a fully qualified test name and a tracking issue number.");

        return problems;
    }

    private static (string[] Pr, string[] Integration) UnitsOf(
        TestTierManifest manifest, IReadOnlyDictionary<string, TestTierProject> entries, IReadOnlySet<string> containers)
    {
        IEnumerable<string> Units(string project) => manifest.Splits.TryGetValue(project, out var split)
            ? split.Groups.Select(group => $"{project}#{group.Name}")
            : [project];
        var sharded = entries.Values
            .Where(entry => !containers.Contains(entry.Project) && !manifest.OwnJobs.ContainsKey(entry.Project))
            .ToArray();
        return (
            sharded.Where(entry => entry.Tier == TestTierManifest.PrTier).SelectMany(entry => Units(entry.Project)).ToArray(),
            sharded.Where(entry => entry.Tier == TestTierManifest.IntegrationTier).SelectMany(entry => Units(entry.Project)).ToArray());
    }

    private static void CheckShards(List<string> problems, string tier, IReadOnlyList<TestTierShard> shards, IReadOnlyCollection<string> expected)
    {
        if (shards.Select(shard => shard.Name).Distinct(StringComparer.Ordinal).Count() != shards.Count)
            problems.Add($"The {tier} shards repeat a name.");
        CheckCoverage(problems, tier, shards.SelectMany(shard => shard.Units).ToArray(), expected);
    }

    private static void CheckCoverage(List<string> problems, string tier, IReadOnlyCollection<string> listed, IReadOnlyCollection<string> expected)
    {
        var expectedSet = expected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in listed.GroupBy(unit => unit, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            problems.Add($"{unit.Key} is in more than one {tier} shard.");
        foreach (var unit in listed.Where(unit => !expectedSet.Contains(unit)).Distinct(StringComparer.OrdinalIgnoreCase))
            problems.Add($"{unit} is in a {tier} shard but is not a {tier}-tier unit. Move it to the shards of its tier.");
        foreach (var unit in expectedSet.Except(listed, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            problems.Add($"{unit} is in no {tier} shard, so it would run nowhere. Add it to one.");
    }

    /// <summary>The project itself plus every test-tree project it references, transitively.</summary>
    private static IEnumerable<string> Closure(string project, IReadOnlyDictionary<string, IReadOnlyList<string>> references)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project };
        var queue = new Queue<string>([project]);
        while (queue.TryDequeue(out var current))
            foreach (var reference in references.TryGetValue(current, out var next) ? next : [])
                if (RepoLayout.IsTestProjectPath(reference) && seen.Add(reference))
                    queue.Enqueue(reference);
        return seen;
    }

    /// <summary>Maps each project under the test roots to the C# files it owns: those whose nearest project folder is its own.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SourcesByProject(RepoContext repo, IReadOnlyList<string> projectFiles)
    {
        var byFolder = projectFiles.ToDictionary(path => Folder(path), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in repo.ListFiles("Essentials/tests/*.cs", "Extensions/*/tests/*.cs").Where(RepoLayout.IsTestPath))
        {
            for (var folder = Folder(file); folder.Length > 0; folder = Folder(folder))
            {
                if (!byFolder.TryGetValue(folder, out var owner))
                    continue;
                if (!result.TryGetValue(owner, out var files))
                    result[owner] = files = [];
                files.Add(file);
                break;
            }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Folder(string path) => path.LastIndexOf('/') is var index and > 0 ? path[..index] : "";

    /// <summary>True when a file starts a process and names the dotnet host: "dotnet", DOTNET_HOST_PATH or a DotnetMuxer helper.</summary>
    public static bool LaunchesDotnet(string source) => ProcessLaunch().IsMatch(source) && DotnetHost().IsMatch(source);

    /// <summary>Top-level classes by fully qualified name, each with the xUnit collection it declares, if any.</summary>
    /// <remarks>
    /// A source scan, not a compiler: a class is a <c>class</c> or <c>record</c> declaration indented by at most four
    /// spaces, which covers file-scoped and block-scoped namespaces. Its <c>[Collection(...)]</c> is read from the attribute
    /// and doc-comment lines directly above it. <c>Name</c> and <c>Type.Name</c> arguments resolve through the
    /// <c>const string Name</c> declared in that class's own body.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> ReadClasses(IReadOnlyList<string> sources)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        var declared = new List<(string Name, string Simple, string? Argument)>();
        foreach (var source in sources)
        {
            var ns = Namespace().Match(source) is { Success: true } match ? match.Groups[1].Value + "." : "";
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var classLines = Enumerable.Range(0, lines.Length).Where(index => ClassDeclaration().IsMatch(lines[index])).ToArray();
            for (var position = 0; position < classLines.Length; position++)
            {
                var line = classLines[position];
                var simple = ClassDeclaration().Match(lines[line]).Groups["name"].Value;
                string? argument = null;
                for (var above = line - 1; above >= 0 && lines[above].TrimStart() is var text && (text.StartsWith('[') || text.StartsWith("//", StringComparison.Ordinal)); above--)
                    if (CollectionAttribute().Match(text) is { Success: true } attribute)
                        argument = attribute.Groups[1].Value.Trim();
                declared.Add((ns + simple, simple, argument));

                var bodyEnd = position + 1 < classLines.Length ? classLines[position + 1] : lines.Length;
                var body = string.Join('\n', lines[line..bodyEnd]);
                if (NameConstant().Match(body) is { Success: true } constant)
                    constants.TryAdd(simple, constant.Groups[1].Value);
            }
        }

        string? Resolve(string simple, string? argument) => argument switch
        {
            null => null,
            _ when argument.StartsWith('"') => argument.Trim('"'),
            "Name" => constants.GetValueOrDefault(simple, $"{simple}.Name"),
            _ when argument.EndsWith(".Name", StringComparison.Ordinal) =>
                constants.GetValueOrDefault(argument[..^".Name".Length].Split('.')[^1], argument),
            _ => argument
        };

        return declared
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => Resolve(item.Simple, item.Argument)).FirstOrDefault(value => value is not null),
                StringComparer.Ordinal);
    }

    [GeneratedRegex("""<PackageReference\s+Include="Microsoft\.NET\.Test\.Sdk"\s""", RegexOptions.CultureInvariant)]
    private static partial Regex TestSdkReference();

    [GeneratedRegex(@"ProcessStartInfo\s*\(|Process\.Start\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessLaunch();

    [GeneratedRegex(@"""dotnet""|DOTNET_HOST_PATH|DotnetMuxer\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DotnetHost();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Namespace();

    [GeneratedRegex(@"^ {0,4}(?:(?:public|internal|sealed|static|abstract|partial|file)\s+)*(?:class|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"\[\s*Collection\s*\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionAttribute();

    [GeneratedRegex(@"const\s+string\s+Name\s*=\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex NameConstant();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.+]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TestName();
}

/// <summary>The facts about the tree that <see cref="TestTierCheck.Validate"/> compares the manifest with.</summary>
public sealed record TestTierFacts(
    IReadOnlyCollection<string> TestProjects,
    IReadOnlyCollection<string> TestcontainersRoots,
    IReadOnlyCollection<string> HostLaunchers,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> ClassesByProject);
