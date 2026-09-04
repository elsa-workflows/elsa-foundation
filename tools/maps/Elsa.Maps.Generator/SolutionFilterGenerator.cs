using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.Maps.Generator;

/// <summary>
/// Generates committed developer solution filters from named project selectors and the actual
/// <c>ProjectReference</c> graph rooted in <c>Elsa.Server.slnx</c>.
/// </summary>
public static class SolutionFilterGenerator
{
    private const string ManifestPath = "tools/solution-filters/profiles.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions FilterJsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Generates every configured filter and returns its repository-relative path.</summary>
    public static IReadOnlyList<string> Generate(RepoContext repo, string? destinationRoot = null)
    {
        var manifest = ReadManifest(repo);
        var solution = ReadSolution(repo, manifest.SolutionPath);
        var outputRoot = destinationRoot ?? repo.Root;
        var written = new List<string>();

        foreach (var profile in manifest.Profiles)
        {
            ValidateProfile(profile);

            var roots = solution.Projects.Values
                .Where(project => Matches(project, profile))
                .Where(project => profile.RequireProjectFileContains.Count == 0 ||
                                  profile.RequireProjectFileContains.Any(project.Content.Contains))
                .Where(project => !profile.ExcludeWhenProjectFileContains.Any(project.Content.Contains))
                .Select(project => project.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (roots.Length == 0)
                throw new InvalidOperationException($"Solution filter profile '{profile.OutputPath}' selected no projects.");

            var projects = ExpandDependencies(solution.Projects, roots);
            var document = new
            {
                solution = new
                {
                    path = manifest.SolutionPath,
                    projects
                }
            };

            var outputPath = Normalize(profile.OutputPath);
            var absoluteOutputPath = Path.Combine(outputRoot, outputPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath)!);
            File.WriteAllText(absoluteOutputPath, JsonSerializer.Serialize(document, FilterJsonOptions) + "\n");
            written.Add(outputPath);
        }

        return written;
    }

    /// <summary>Returns zero when committed filters match freshly generated output, one otherwise.</summary>
    public static int Check(RepoContext repo)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"elsa-solution-filters-check-{Environment.ProcessId}");

        try
        {
            Directory.CreateDirectory(scratch);
            var generated = Generate(repo, scratch);
            var stale = generated
                .Where(path => !SameBytes(repo.Absolute(path), Path.Combine(scratch, path.Replace('/', Path.DirectorySeparatorChar))))
                .Concat(Directory.GetFiles(repo.Root, "Elsa.Server.*.slnf", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(path => !generated.Contains(path, StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (stale.Length == 0)
            {
                Console.WriteLine("Generated solution filters still describe the project graph.");
                return 0;
            }

            Console.Error.WriteLine(
                $"""
                 Generated solution filters no longer describe the project graph. {stale.Length} file(s) would change:

                 {string.Join(Environment.NewLine, stale.Select(path => "  " + path))}

                 Refresh them and commit the result:

                   dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters
                 """);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static SolutionFilterManifest ReadManifest(RepoContext repo)
    {
        var path = repo.Absolute(ManifestPath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Solution filter manifest not found: {ManifestPath}");

        var manifest = JsonSerializer.Deserialize<SolutionFilterManifest>(File.ReadAllText(path), ManifestJsonOptions)
                       ?? throw new InvalidOperationException($"Solution filter manifest is empty: {ManifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.SolutionPath))
            throw new InvalidOperationException("Solution filter manifest must define solutionPath.");
        if (manifest.Profiles.Count == 0)
            throw new InvalidOperationException("Solution filter manifest must define at least one profile.");

        var duplicateOutput = manifest.Profiles
            .GroupBy(profile => Normalize(profile.OutputPath), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOutput is not null)
            throw new InvalidOperationException($"Solution filter manifest defines output '{duplicateOutput.Key}' more than once.");

        return manifest;
    }

    private static SolutionGraph ReadSolution(RepoContext repo, string solutionPath)
    {
        var absoluteSolutionPath = repo.Absolute(Normalize(solutionPath));
        if (!File.Exists(absoluteSolutionPath))
            throw new InvalidOperationException($"Solution file not found: {solutionPath}");

        var document = XDocument.Load(absoluteSolutionPath);
        var projectPaths = document.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Normalize(path!))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var projects = new Dictionary<string, SolutionProject>(StringComparer.Ordinal);
        foreach (var projectPath in projectPaths)
        {
            var absoluteProjectPath = repo.Absolute(projectPath);
            if (!File.Exists(absoluteProjectPath))
                throw new InvalidOperationException($"Solution project not found: {projectPath}");

            var content = File.ReadAllText(absoluteProjectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            projects.Add(projectPath, new SolutionProject(projectName, projectPath, content, []));
        }

        foreach (var project in projects.Values.ToArray())
        {
            var projectDocument = XDocument.Parse(project.Content);
            var projectDirectory = Path.GetDirectoryName(repo.Absolute(project.Path))!;
            var includes = projectDocument.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!)
                .ToArray();

            if (includes.FirstOrDefault(include => include.Contains("$(", StringComparison.Ordinal)) is { } dynamicInclude)
                throw new InvalidOperationException($"Project '{project.Path}' has an unresolved ProjectReference: {dynamicInclude}");

            var references = includes
                .Select(include => Path.GetFullPath(include!.Replace('\\', Path.DirectorySeparatorChar), projectDirectory))
                .Select(absolute => Normalize(Path.GetRelativePath(repo.Root, absolute)))
                .Where(projects.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            projects[project.Path] = project with { References = references };
        }

        return new SolutionGraph(projects);
    }

    private static void ValidateProfile(SolutionFilterProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.OutputPath) || !profile.OutputPath.EndsWith(".slnf", StringComparison.Ordinal))
            throw new InvalidOperationException("Every solution filter profile must define an outputPath ending in .slnf.");

        var normalizedOutputPath = Normalize(profile.OutputPath);
        if (Path.IsPathRooted(profile.OutputPath) || normalizedOutputPath.Split('/').Contains("..", StringComparer.Ordinal))
            throw new InvalidOperationException($"Solution filter output '{profile.OutputPath}' must stay inside the repository.");
        if (normalizedOutputPath.Contains('/', StringComparison.Ordinal))
            throw new InvalidOperationException($"Solution filter output '{profile.OutputPath}' must be written at the repository root.");

        if (profile.IncludeProjectNames.Count == 0 &&
            profile.IncludeProjectNamePrefixes.Count == 0 &&
            profile.IncludeProjectNameContains.Count == 0)
            throw new InvalidOperationException($"Solution filter profile '{profile.OutputPath}' must define at least one project-name selector.");
    }

    private static bool Matches(SolutionProject project, SolutionFilterProfile profile) =>
        profile.IncludeProjectNames.Contains(project.Name, StringComparer.Ordinal) ||
        profile.IncludeProjectNamePrefixes.Any(project.Name.StartsWith) ||
        profile.IncludeProjectNameContains.Any(project.Name.Contains);

    private static IReadOnlyList<string> ExpandDependencies(
        IReadOnlyDictionary<string, SolutionProject> projects,
        IReadOnlyList<string> roots)
    {
        var selected = new HashSet<string>(roots, StringComparer.Ordinal);
        var pending = new Queue<string>(roots);

        while (pending.TryDequeue(out var path))
        {
            foreach (var dependency in projects[path].References)
            {
                if (selected.Add(dependency))
                    pending.Enqueue(dependency);
            }
        }

        return selected.Order(StringComparer.Ordinal).ToArray();
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static bool SameBytes(string left, string right) =>
        File.Exists(left) && File.Exists(right) && File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));

    private sealed record SolutionGraph(IReadOnlyDictionary<string, SolutionProject> Projects);
    private sealed record SolutionProject(string Name, string Path, string Content, IReadOnlyList<string> References);
}

public sealed class SolutionFilterManifest
{
    public string SolutionPath { get; init; } = string.Empty;
    public IReadOnlyList<SolutionFilterProfile> Profiles { get; init; } = [];
}

public sealed class SolutionFilterProfile
{
    public string OutputPath { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludeProjectNames { get; init; } = [];
    public IReadOnlyList<string> IncludeProjectNamePrefixes { get; init; } = [];
    public IReadOnlyList<string> IncludeProjectNameContains { get; init; } = [];
    public IReadOnlyList<string> RequireProjectFileContains { get; init; } = [];
    public IReadOnlyList<string> ExcludeWhenProjectFileContains { get; init; } = [];
}
