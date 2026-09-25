using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.Maps.Generator;

/// <summary>Selects EF container jobs from the same validated project graph as solution filters.</summary>
public static partial class EfSuiteSelector
{
    private const string ManifestPath = "tools/ci/ef-suites.json";
    private const string IntegrationFilterPath = "Elsa.Server.Persistence.Integration.slnf";
    private const string SeparateSecretsProject = "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static EfSuiteSelection Select(RepoContext repo, string eventName, string? baseSha, string? headSha)
    {
        var suites = ReadSuites(repo);
        if (eventName is "push" or "workflow_dispatch")
            return Full(suites, $"{eventName} always runs the full EF matrix");
        if (eventName != "pull_request")
            throw new InvalidOperationException($"Unsupported EF selection event: {eventName}");

        if (!CommitPattern().IsMatch(baseSha ?? "") || !CommitPattern().IsMatch(headSha ?? ""))
            return Full(suites, "PR base/head commits are unavailable");

        try
        {
            var changedPaths = ReadChangedPaths(repo, baseSha!, headSha!);
            var addedOrDeletedProjects = ReadChangedPaths(repo, baseSha!, headSha!, "AD")
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (addedOrDeletedProjects.Length > 0)
                return Full(suites, "project added or removed in the PR diff");
            var projects = SolutionFilterGenerator.GetProjectReferences(repo);
            var linkedSources = ReadLinkedSourceOwners(repo, projects.Keys);
            return SelectPaths(suites, projects, changedPaths, linkedSources);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        {
            // A broken diff or graph must never turn an uncertain PR into a zero-suite run.
            return Full(suites, $"impact could not be calculated: {exception.GetType().Name}");
        }
    }

    public static EfSuiteSelection SelectPaths(
        IReadOnlyList<EfSuite> suites,
        IReadOnlyDictionary<string, IReadOnlyList<string>> projects,
        IReadOnlyList<string> changedPaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? linkedSourceOwners = null)
    {
        if (changedPaths.Count == 0)
            return new EfSuiteSelection("none", "PR diff is empty", []);

        var changedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in changedPaths)
        {
            var path = Normalize(rawPath);
            if (IsDocumentation(path))
                continue;
            if (MustRunFull(path))
                return Full(suites, $"shared or selector input changed: {path}");
            if (!path.StartsWith("src/", StringComparison.Ordinal) &&
                !path.StartsWith("tests/", StringComparison.Ordinal))
                return Full(suites, $"unclassified path changed: {path}");
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !projects.ContainsKey(path))
                return Full(suites, $"unmapped project changed: {path}");

            var owners = projects.Keys
                .Where(project => path.StartsWith(DirectoryOf(project), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            IReadOnlyList<string>? linkedOwners = null;
            linkedSourceOwners?.TryGetValue(path, out linkedOwners);
            if (owners.Length == 0 && (linkedOwners is null || linkedOwners.Count == 0))
                return Full(suites, $"unmapped source/test path changed: {path}");
            // A parent SDK project can compile a file below a nested project directory too.
            // Include every containing project so the selector cannot omit that parent consumer.
            changedProjects.UnionWith(owners);
            if (linkedOwners is not null)
                changedProjects.UnionWith(linkedOwners);
        }

        if (changedProjects.Count == 0)
            return new EfSuiteSelection("none", "documentation-only PR", []);

        var selected = suites
            .Where(suite => DependencyClosure(projects, suite.Project).Overlaps(changedProjects))
            .ToArray();
        return selected.Length == 0
            ? new EfSuiteSelection("none", "changed projects have no EF suite consumers", [])
            : new EfSuiteSelection("selected", "changed projects reach these EF suites", selected);
    }

    private static IReadOnlyList<EfSuite> ReadSuites(RepoContext repo)
    {
        var manifest = JsonSerializer.Deserialize<EfSuiteManifest>(File.ReadAllText(repo.Absolute(ManifestPath)), JsonOptions)
                       ?? throw new InvalidOperationException("EF suite manifest is empty");
        var suites = manifest.Suites;
        if (suites.Count == 0 || suites.Any(suite =>
                suite.Suite is null || !SuitePattern().IsMatch(suite.Suite) ||
                suite.Project is null || !ProjectPattern().IsMatch(suite.Project) ||
                suite.Project.Split('/').Contains("..", StringComparer.Ordinal) ||
                !suite.Project.StartsWith("tests/", StringComparison.Ordinal) &&
                !suite.Project.StartsWith("src/extensions/", StringComparison.Ordinal) ||
                !File.Exists(repo.Absolute(suite.Project)) ||
                suite.Require is null || !RequirePattern().IsMatch(suite.Require)))
            throw new InvalidOperationException("EF suite manifest contains an invalid entry");
        if (suites.Select(suite => suite.Suite).Distinct(StringComparer.Ordinal).Count() != suites.Count ||
            suites.Select(suite => suite.Project).Distinct(StringComparer.OrdinalIgnoreCase).Count() != suites.Count)
            throw new InvalidOperationException("EF suite manifest contains duplicate suites or projects");
        var solutionProjects = SolutionFilterGenerator.GetProjectReferences(repo).Keys.ToArray();
        var allTestProjects = Directory.GetFiles(repo.Absolute("tests"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(repo.Absolute("src"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Normalize(path).Contains("/tests/", StringComparison.OrdinalIgnoreCase)))
            .Select(path => Normalize(Path.GetRelativePath(repo.Root, path)))
            .ToArray();
        ValidateCoverage(suites, SolutionFilterGenerator.GetRoots(repo, IntegrationFilterPath),
            allTestProjects, solutionProjects);
        return suites.OrderBy(suite => suite.Suite, StringComparer.Ordinal).ToArray();
    }

    public static void ValidateCoverage(
        IReadOnlyList<EfSuite> suites,
        IReadOnlyCollection<string> integrationRoots,
        IReadOnlyCollection<string> allTestProjects,
        IReadOnlyCollection<string> solutionProjects)
    {
        var listed = solutionProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlistedTests = allTestProjects.Except(listed, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unlistedTests.Length > 0)
            throw new InvalidOperationException($"Test projects absent from the solution: {string.Join(", ", unlistedTests)}");

        if (suites.Any(suite => string.Equals(suite.Project, SeparateSecretsProject, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The separate Secrets EF host proof must not be duplicated in the matrix");
        var expected = suites.Select(suite => suite.Project)
            .Append(SeparateSecretsProject)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = integrationRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(actual))
            throw new InvalidOperationException(
                $"EF suite manifest does not match Testcontainers roots. Unmapped: {string.Join(", ", actual.Except(expected))}; obsolete: {string.Join(", ", expected.Except(actual))}");
    }

    private static IReadOnlyList<string> ReadChangedPaths(RepoContext repo, string baseSha, string headSha, string? diffFilter = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        // Treat moves as a deletion plus an addition so both old and new owners are checked.
        foreach (var argument in new[] { "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff" })
            process.StartInfo.ArgumentList.Add(argument);
        if (diffFilter is not null)
            process.StartInfo.ArgumentList.Add($"--diff-filter={diffFilter}");
        process.StartInfo.ArgumentList.Add(baseSha);
        process.StartInfo.ArgumentList.Add(headSha);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff failed: {error.Trim()}");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadLinkedSourceOwners(
        RepoContext repo,
        IEnumerable<string> projectPaths)
    {
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(repo.Absolute(project))!;
            var document = XDocument.Load(repo.Absolute(project));
            foreach (var include in document.Descendants()
                         .Where(element => element.Name.LocalName == "Compile")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (include!.Contains("$(", StringComparison.Ordinal) ||
                    include.Contains("@(", StringComparison.Ordinal) ||
                    include.IndexOfAny(['*', '?', ';']) >= 0)
                    throw new InvalidOperationException($"Unresolved Compile Include in {project}");
                var absolute = Path.GetFullPath(include.Replace('\\', Path.DirectorySeparatorChar), projectDirectory);
                var relative = Normalize(Path.GetRelativePath(repo.Root, absolute));
                if (relative.StartsWith("../", StringComparison.Ordinal) || !File.Exists(absolute))
                    throw new InvalidOperationException($"Compile Include is outside the repository or missing: {project}");
                if (!owners.TryGetValue(relative, out var projects))
                    owners.Add(relative, projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                projects.Add(project);
            }
        }
        return owners.ToDictionary(owner => owner.Key,
            owner => (IReadOnlyList<string>)owner.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> DependencyClosure(
        IReadOnlyDictionary<string, IReadOnlyList<string>> projects,
        string suiteProject)
    {
        if (!projects.ContainsKey(suiteProject))
            throw new InvalidOperationException($"EF suite project is missing from the solution: {suiteProject}");
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(suiteProject);
        while (pending.TryPop(out var project))
        {
            if (!closure.Add(project))
                continue;
            if (!projects.TryGetValue(project, out var references))
                throw new InvalidOperationException($"Project reference is missing from the solution: {project}");
            foreach (var reference in references)
                pending.Push(reference);
        }
        return closure;
    }

    private static bool IsDocumentation(string path) =>
        path.StartsWith("docs/", StringComparison.Ordinal) ||
        (path.StartsWith("specs/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) ||
        path is "README.md" or "AGENTS.md" or "EXTENSION_POINTS.md";

    private static bool MustRunFull(string path) =>
        path.StartsWith(".github/", StringComparison.Ordinal) ||
        path.StartsWith("tools/", StringComparison.Ordinal) ||
        path.StartsWith("src/apps/", StringComparison.Ordinal) ||
        path.StartsWith("src/essentials/Persistence/EntityFramework", StringComparison.Ordinal) ||
        path.StartsWith("src/essentials/Modularity/", StringComparison.Ordinal) ||
        path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        path is "global.json" or "NuGet.Config";

    private static EfSuiteSelection Full(IReadOnlyList<EfSuite> suites, string reason) =>
        new("full", reason, suites);

    private static string DirectoryOf(string project) =>
        Normalize(Path.GetDirectoryName(project)!) + "/";

    private static string Normalize(string path) => path.Replace('\\', '/');

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^$|^ELSA_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RequirePattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SuitePattern();

    [GeneratedRegex("^[A-Za-z0-9_./-]+\\.csproj$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPattern();
}

public sealed record EfSuite(string Suite, string Project, string Require);
public sealed record EfSuiteSelection(string Mode, string Reason, IReadOnlyList<EfSuite> Suites)
{
    public string ToJson() => JsonSerializer.Serialize(new
    {
        mode = Mode,
        reason = Reason,
        count = Suites.Count,
        matrix = new { include = Suites.Select(suite => new
        {
            suite = suite.Suite,
            project = suite.Project,
            require = suite.Require
        }).ToArray() }
    });
}

public sealed class EfSuiteManifest
{
    public IReadOnlyList<EfSuite> Suites { get; init; } = [];
}
