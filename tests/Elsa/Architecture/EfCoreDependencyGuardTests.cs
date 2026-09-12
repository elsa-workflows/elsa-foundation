using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0073 selects EF Core as the destination persistence family, but each implementation still enters
/// through an explicitly reviewed program issue. This ratchet therefore keeps the currently admitted
/// surface at the vendor-owned OpenIddict host boundary plus ADR 0072's accepted Secrets EF tree. It reads
/// each source project's evaluated restore graph and scans sources, so imported, conditional, transitive,
/// and provider-only EF edges anywhere else under <c>src/</c> fail and name the offender. Each replacement
/// changes this guard deliberately with its own architecture evidence; the destination ADR alone is not a
/// repository-wide exemption.
/// </summary>
public sealed class EfCoreDependencyGuardTests
{
    private static readonly string[] AllowedEfConsumers = ["Elsa.Workbench"];
    private const string EfPackageToken = "EntityFrameworkCore";

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Only_admitted_consumers_and_pilot_projects_resolve_ef_core_packages()
    {
        var projects = LoadSrcProjects();

        Assert.All(AllowedEfConsumers, name => Assert.Contains(name, projects.Keys));

        var offenders = projects.Values
            .Where(project => project.ResolvesEf && !project.IsPilot)
            .Select(project => project.Name)
            .Except(AllowedEfConsumers)
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("resolve EF Core outside the admitted consumers and pilot projects", offenders));
    }

    [Fact]
    public void Evaluated_assets_detect_an_imported_provider_only_dependency()
    {
        const string assets = """
            {
              "libraries": {
                "Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0": { "type": "package" }
              }
            }
            """;

        Assert.True(ResolvesEfCore(assets));
    }

    [Fact]
    public void No_source_file_outside_the_admitted_surfaces_mentions_ef_core()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !IsAdmittedEfSource(file))
            .Where(file => File.ReadAllText(file).Contains(EfPackageToken, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("mention an EF package namespace in source", offenders));
    }

    private static Dictionary<string, Project> LoadSrcProjects()
    {
        var projects = new Dictionary<string, Project>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            var relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            var assetsPath = Path.Combine(Path.GetDirectoryName(file)!, "obj", "project.assets.json");
            if (!File.Exists(assetsPath))
                throw new InvalidOperationException($"Evaluated restore assets are required for '{relativePath}'. Restore Elsa.Server.slnx before running the EF dependency guard.");

            projects.Add(
                Path.GetFileNameWithoutExtension(file),
                new Project(Path.GetFileNameWithoutExtension(file), ResolvesEfCore(File.ReadAllText(assetsPath)), Adr0072SecretsEfPilot.IsProjectPath(relativePath)));
        }
        return projects;
    }

    private static bool ResolvesEfCore(string assetsJson)
    {
        using var document = JsonDocument.Parse(assetsJson);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            return false;

        return libraries.EnumerateObject()
            .Any(library => library.Name.Split('/', 2)[0].Contains(EfPackageToken, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAdmittedEfSource(string file)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
        return Adr0072SecretsEfPilot.IsSurfacePath(relativePath) ||
               OpenIddictPersistenceArchitectureTests.IsWorkbenchVendorEfSource(relativePath);
    }

    private static bool IsBuildOutput(string path) => path.Replace('\\', '/') is var p && (p.Contains("/bin/") || p.Contains("/obj/"));

    private static string Report(string what, IEnumerable<string> offenders) =>
        $"These src entries {what}:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record Project(string Name, bool ResolvesEf, bool IsPilot);

    /// <summary>
    /// ADR 0072's accepted, now-superseded Secrets EF surface. ADR 0073 preserves this as the only currently
    /// admitted first-party implementation while later Program #1665 replacements remain evidence-gated.
    /// <see cref="SecretsEfPersistencePilotArchitectureTests"/> owns the exact package and source inventory.
    /// </summary>
    internal static class Adr0072SecretsEfPilot
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "src/Elsa/Persistence/EntityFramework/",
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/",
            "tests/Elsa/Persistence/EntityFramework/",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/"
        ];

        public static readonly string[] ProjectPaths =
        [
            "src/Elsa/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj",
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj",
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj",
            "tests/Elsa/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj"
        ];

        public static bool IsSurfacePath(string relativePath) =>
            SurfacePathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);
    }
}
