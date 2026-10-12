using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// EF Core is the only first-party persistence family. The retired storage library and the retired
/// document-database driver were deleted, not deprecated, so nothing under <c>Essentials/</c> or <c>Extensions/</c>,
/// <c>tools/</c>, <c>docker/</c>, <c>e2e-tests/</c>, <c>.github/</c> or the repository-level package and
/// feed configuration may name either of them again: no project, no package, no NuGet feed, no build or CI
/// configuration, no directory and no solution filter.
/// </summary>
/// <remarks>
/// <para>
/// This guard replaces the family-specific architecture guards it supersedes (the design boundary,
/// bounded-query, ledger-isolation, identity-mutation and provider-parsing ratchets). Those guards each
/// constrained how the retired family could be used; there is nothing left to constrain, and the one
/// property that still matters is total absence.
/// </para>
/// <para>
/// It reads project, package, feed, build, container and CI files, never C# source (framework §2.23.2). Every file
/// kind that can carry a package or project reference is scanned, including the repository-root build files and
/// Dockerfiles. A source-only reintroduction (a type or string that names a retired family without a reference) is
/// left to code review.
/// </para>
/// <para>
/// <b>Fail-closed.</b> The forbidden names are assembled at runtime from fragments so this file is not
/// itself a match and needs no self-exclusion — an exclusion list is exactly the hole a real reference
/// would hide in. Every scan asserts it examined a non-empty file set, so a mistyped root, a renamed
/// directory or a repository-root lookup that walks past the tree fails the guard instead of passing it
/// vacuously. <see cref="Guard_detects_a_synthetic_reference_in_each_scanned_shape"/> proves the
/// detector by feeding the guard's own predicates a violating input.
/// </para>
/// </remarks>
public sealed class RetiredPersistenceFamilyGuardTests
{
    /// <summary>The retired family names, assembled so this source file is not itself a violation.</summary>
    private static readonly string[] RetiredNames =
    [
        string.Concat("Ground", "work"),
        string.Concat("Mon", "go")
    ];

    /// <summary>Roots that ship or build the product, including Studio as a sibling product under Extensions/. History under docs/, specs/ and archives is out of scope.</summary>
    private static readonly string[] ScannedRoots = ["Essentials", "Extensions", "tools", "samples", "docker", "e2e-tests", ".github", ".config"];

    /// <summary>Scanned by name wherever they sit; repository-root files of a scanned extension are read too.</summary>
    private static readonly string[] ScannedFiles = ["Directory.Packages.props", "NuGet.config", "Elsa.Server.slnx", "Dockerfile"];

    /// <summary>The path ledger preserves retired names only to translate the historical tree during migration.</summary>
    private const string HistoricalMigrationLedger = "tools/consolidation/foundation-path-map.json";

    /// <summary>Project, package, feed, build and CI files. C# source and markdown are deliberately not read.</summary>
    private static readonly string[] ScannedExtensions =
    [
        ".csproj", ".props", ".targets", ".json", ".slnx", ".slnf", ".yml", ".yaml",
        ".config", ".sh", ".ps1", ".py", ".sql", ".Dockerfile"
    ];

    [Fact]
    public void No_project_package_feed_or_configuration_names_a_retired_persistence_family()
    {
        var files = ScannedConfigurationFiles().ToArray();

        Assert.True(files.Length > 0, "The retired-family guard scanned no files; its roots or extensions are wrong.");
        Assert.Contains("Directory.Packages.props", files.Select(file => file.RelativePath));
        Assert.Contains("NuGet.config", files.Select(file => file.RelativePath));
        Assert.Contains("Directory.Build.props", files.Select(file => file.RelativePath));

        var violations = Violations(files.Select(file => (file.RelativePath, File.ReadAllText(file.Path))));
        Assert.True(
            violations.Count == 0,
            $"EF Core is the only first-party persistence family. {violations.Count} file(s) reintroduce a retired one:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void No_project_or_directory_is_named_after_a_retired_persistence_family()
    {
        var scannedProjects = 0;
        var violations = new List<string>();

        foreach (var root in ScannedRoots)
        {
            var rootPath = FullPath(root);
            if (!Directory.Exists(rootPath))
                continue;

            foreach (var project in Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOrPackageOutput(path)))
            {
                scannedProjects++;
                var relativePath = RelativePath(project);
                if (NamesRetiredFamily(relativePath))
                    violations.Add($"{relativePath}: project path names a retired persistence family");
            }

            foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOrPackageOutput(path)))
            {
                var relativePath = RelativePath(directory);
                if (NamesRetiredFamily(Path.GetFileName(relativePath)))
                    violations.Add($"{relativePath}: directory names a retired persistence family");
            }
        }

        Assert.True(scannedProjects > 0, "The retired-family guard found no projects; its roots are wrong.");
        Assert.True(
            violations.Count == 0,
            "No project or directory may be named after a retired persistence family:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void No_solution_filter_selects_a_retired_persistence_family()
    {
        var filters = Directory.EnumerateFiles(RepoRoot, "*.slnf", SearchOption.TopDirectoryOnly).ToArray();

        Assert.NotEmpty(filters);
        foreach (var filter in filters)
        {
            var relativePath = RelativePath(filter);
            Assert.False(NamesRetiredFamily(relativePath) || NamesRetiredFamily(File.ReadAllText(filter)), $"{relativePath} selects a retired persistence family.");
        }
    }

    /// <summary>
    /// Mutation proof. Each shape the guards scan — a central package pin, a feed, a package reference, a project
    /// path, a directory name — is fed a synthetic violation through the same predicates the guards above use, and
    /// must be flagged. A C# file is not a scanned shape, and a clean input must not trip anything. Without this a
    /// scan that silently matched nothing would read as a pass.
    /// </summary>
    [Fact]
    public void Guard_detects_a_synthetic_reference_in_each_scanned_shape()
    {
        var retired = RetiredNames[0];
        (string RelativePath, string Text)[] files =
        [
            ("Directory.Packages.props", $"""<Project><ItemGroup><PackageVersion Include="{retired}.Sqlite" Version="1.0.0" /></ItemGroup></Project>"""),
            ("NuGet.config", $"""<configuration><packageSources><add key="{retired}" value="https://feed.example/{retired}" /></packageSources></configuration>"""),
            ("src/Module/Elsa.Module.csproj", $"""<Project><ItemGroup><PackageReference Include="{retired}.Kernel" /></ItemGroup></Project>"""),
            (".github/workflows/ci.yml", $"run: dotnet test tests/{retired}"),
            ("src/Module/Clean.csproj", """<Project><ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup></Project>""")
        ];

        Assert.All(files, file => Assert.True(IsScannedConfigurationFile(file.RelativePath), $"{file.RelativePath} is not scanned."));
        Assert.Equal(
            ["Directory.Packages.props", "NuGet.config", "src/Module/Elsa.Module.csproj", ".github/workflows/ci.yml"],
            Violations(files).Select(violation => violation[..violation.IndexOf(':')]));

        Assert.True(NamesRetiredFamily($"Essentials/src/Persistence/{retired}/Elsa.Persistence.{retired}.csproj"));
        Assert.True(NamesRetiredFamily(retired));
        Assert.False(NamesRetiredFamily("Essentials/src/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"));
        Assert.False(IsScannedConfigurationFile("src/Module/Store.cs"));
        Assert.False(IsScannedConfigurationFile("src/Module/README.md"));
    }

    [Fact]
    public void Only_the_foundation_historical_path_ledger_is_exempt_from_configuration_content_scanning()
    {
        Assert.True(IsHistoricalMigrationLedger("tools/consolidation/foundation-path-map.json"));
        Assert.False(IsHistoricalMigrationLedger("tools/consolidation/studio-path-map.json"));
        Assert.False(IsHistoricalMigrationLedger("tools/other/foundation-path-map.json"));
    }

    private static bool NamesRetiredFamily(string text) =>
        RetiredNames.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsScannedConfigurationFile(string relativePath) =>
        ScannedFiles.Contains(Path.GetFileName(relativePath), StringComparer.Ordinal) ||
        ScannedExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase);

    private static bool IsHistoricalMigrationLedger(string relativePath) =>
        string.Equals(relativePath, HistoricalMigrationLedger, StringComparison.Ordinal);

    private static List<string> Violations(IEnumerable<(string RelativePath, string Text)> files) =>
        [.. files
            .Where(file => NamesRetiredFamily(file.Text))
            .Select(file => $"{file.RelativePath}: names a retired persistence family")];

    private static IEnumerable<(string Path, string RelativePath)> ScannedConfigurationFiles()
    {
        foreach (var root in ScannedRoots)
        {
            var rootPath = FullPath(root);
            if (!Directory.Exists(rootPath))
                continue;

            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = RelativePath(path);
                if (!IsBuildOrPackageOutput(path) && IsScannedConfigurationFile(relativePath) && !IsHistoricalMigrationLedger(relativePath))
                    yield return (path, relativePath);
            }
        }

        foreach (var path in Directory.EnumerateFiles(FullPath("."), "*", SearchOption.TopDirectoryOnly))
        {
            var relativePath = RelativePath(path);
            if (IsScannedConfigurationFile(relativePath))
                yield return (path, relativePath);
        }
    }

    private static bool IsBuildOrPackageOutput(string path) =>
        IsBuildOutput(path) || HasSegment(RepoRoot, path, "node_modules");

    private static string FullPath(string relativePath) => Path.Combine(RepoRoot, relativePath);

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
