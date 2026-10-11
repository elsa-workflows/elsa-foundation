using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>
/// Reads the EF container matrix from the suite manifest and proves the manifest still names every Testcontainers
/// test project of the solution. The matrix runs nightly and on manual dispatch only (#2632), always in full.
/// </summary>
public static partial class EfSuiteSelector
{
    private const string ManifestPath = "tools/ci/ef-suites.json";
    private const string IntegrationFilterPath = "Elsa.Server.Persistence.Integration.slnf";
    private const string SeparateSecretsProject = "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static EfSuiteSelection Select(RepoContext repo)
    {
        var suites = ReadSuites(repo);
        var allTestProjects = Directory.GetFiles(repo.Absolute("tests"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(repo.Absolute("src"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Normalize(path).Contains("/tests/", StringComparison.OrdinalIgnoreCase)))
            .Select(path => Normalize(Path.GetRelativePath(repo.Root, path)))
            .ToArray();
        ValidateCoverage(suites, SolutionFilterGenerator.GetRoots(repo, IntegrationFilterPath),
            allTestProjects, SolutionFilterGenerator.GetProjectReferences(repo).Keys.ToArray());
        return new EfSuiteSelection(suites);
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

    private static string Normalize(string path) => path.Replace('\\', '/');

    [GeneratedRegex("^$|^ELSA_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RequirePattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SuitePattern();

    [GeneratedRegex("^[A-Za-z0-9_./-]+\\.csproj$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPattern();
}

public sealed record EfSuite(string Suite, string Project, string Require);

public sealed record EfSuiteSelection(IReadOnlyList<EfSuite> Suites)
{
    public string ToJson() => JsonSerializer.Serialize(new
    {
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
