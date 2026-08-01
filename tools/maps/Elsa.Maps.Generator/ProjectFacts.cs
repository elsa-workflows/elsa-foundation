using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>One project in the repository, with the grouping labels the maps report.</summary>
/// <param name="Name">Assembly/project name, i.e. the file name without <c>.csproj</c>.</param>
/// <param name="RelativePath">Repo-relative, forward-slashed path to the <c>.csproj</c>.</param>
/// <param name="Kind"><c>source</c> or <c>test</c>.</param>
/// <param name="Domain">Top-level grouping, e.g. <c>Elsa.Workflows</c>.</param>
/// <param name="SubDomain">Remainder after the domain prefix, or <c>-</c> / <c>(root)</c>.</param>
/// <param name="Role">Heuristic navigation label — not a constitution verdict.</param>
/// <param name="References">Directly referenced project names, ordinally sorted and deduplicated.</param>
public sealed record ProjectFacts(
    string Name,
    string RelativePath,
    string Kind,
    string Domain,
    string SubDomain,
    string Role,
    IReadOnlyList<string> References);

/// <summary>Reads the project graph out of the repository's <c>.csproj</c> files.</summary>
public static partial class ProjectGraph
{
    [GeneratedRegex("<ProjectReference[^>]+Include=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex ProjectReferencePattern { get; }

    /// <summary>Reads every source and test project, in ordinal path order.</summary>
    public static IReadOnlyList<ProjectFacts> Read(RepoContext repo) =>
        repo.ListFiles("src/*.csproj", "tests/*.csproj").Select(relativePath => Read(repo, relativePath)).ToArray();

    private static ProjectFacts Read(RepoContext repo, string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var kind = relativePath.StartsWith("tests/", StringComparison.Ordinal) ? "test" : "source";
        var domain = DomainGroup(name);

        return new ProjectFacts(
            name,
            relativePath,
            kind,
            domain,
            SubDomain(name, domain),
            Role(name, kind),
            ReadReferences(repo.Absolute(relativePath)));
    }

    /// <summary>
    /// Referenced project names in ordinal order, with duplicates retained. Callers that report a
    /// deduplicated view apply <c>Distinct</c> themselves; the v1 map layer reports the raw count.
    /// </summary>
    public static IReadOnlyList<string> ReadReferencedProjectNames(string absolutePath) =>
        ProjectReferencePattern.Matches(File.ReadAllText(absolutePath))
            .Select(match => match.Groups[1].Value)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The project whose directory is the longest prefix of <paramref name="relativePath"/>, or null
    /// when the file sits outside every project.
    /// </summary>
    public static TProject? OwningProject<TProject>(
        IReadOnlyList<TProject> projects,
        string relativePath,
        Func<TProject, string> projectPath)
        where TProject : class =>
        projects
            .Select(project => (project, directory: Path.GetDirectoryName(projectPath(project))?.Replace('\\', '/') ?? string.Empty))
            .Where(candidate => relativePath.StartsWith(candidate.directory + "/", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.directory.Length)
            .Select(candidate => candidate.project)
            .FirstOrDefault();

    private static IReadOnlyList<string> ReadReferences(string absolutePath) =>
        ReadReferencedProjectNames(absolutePath).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The top-level grouping a project belongs to.</summary>
    public static string DomainGroup(string project)
    {
        if (project is "Elsa.Server" or "Server") return "Elsa.Server";
        if (project.StartsWith("Test.", StringComparison.Ordinal)) return "Test";
        if (project.StartsWith("Elsa3.", StringComparison.Ordinal)) return "Elsa3";

        if (!project.StartsWith("Elsa.", StringComparison.Ordinal)) return "Other";

        // "Elsa.Workflows.Runtime.Core" groups under "Elsa.Workflows"; a bare "Elsa" keeps its single segment.
        var segments = project.Split('.');
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
    }

    private static string SubDomain(string project, string domain)
    {
        if (domain is "Elsa.Server" or "Test" or "Elsa3" or "Other") return "-";

        var tail = project.StartsWith(domain, StringComparison.Ordinal) ? project[domain.Length..] : project;
        tail = tail.StartsWith('.') ? tail[1..] : tail;
        return tail.Length > 0 ? tail : "(root)";
    }

    private static string Role(string project, string kind)
    {
        if (kind == "test") return "test";
        if (project is "Elsa.Server" or "Server") return "host";
        if (project.EndsWith(".Core", StringComparison.Ordinal)) return "contract";

        if (EndsWithAny(project, ".Strategies", ".Schedules", ".Libraries")) return "helper";
        if (EndsWithAny(project, ".Sqlite", ".EFCore", ".FileSystem", ".Jint", ".Liquid")) return "provider/implementation";

        return "feature/implementation";
    }

    private static bool EndsWithAny(string value, params string[] suffixes) =>
        suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.Ordinal));
}
