using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// EF Core is the only first-party persistence family. The retired storage library and the retired
/// document-database driver were deleted, not deprecated, so nothing under <c>src/</c>, <c>tests/</c>,
/// <c>tools/</c>, <c>docker/</c>, <c>e2e-tests/</c>, <c>.github/</c> or the repository-level package and
/// feed configuration may name either of them again: no project, no package, no NuGet feed, no source
/// file, no shell feature and no CI job.
/// </summary>
/// <remarks>
/// <para>
/// This guard replaces the family-specific architecture guards it supersedes (the design boundary,
/// bounded-query, ledger-isolation, identity-mutation and provider-parsing ratchets). Those guards each
/// constrained how the retired family could be used; there is nothing left to constrain, and the one
/// property that still matters is total absence.
/// </para>
/// <para>
/// <b>Fail-closed.</b> The forbidden names are assembled at runtime from fragments so this file is not
/// itself a match and needs no self-exclusion — an exclusion list is exactly the hole a real reference
/// would hide in. Every scan asserts it examined a non-empty file set, so a mistyped root, a renamed
/// directory or a repository-root lookup that walks past the tree fails the guard instead of passing it
/// vacuously. <see cref="Guard_detects_a_synthetic_reference_in_each_scanned_shape"/> proves the
/// detector by feeding it a violating input.
/// </para>
/// </remarks>
public sealed partial class RetiredPersistenceFamilyGuardTests
{
    /// <summary>The retired family names, assembled so this source file is not itself a violation.</summary>
    private static readonly string[] RetiredNames =
    [
        string.Concat("Ground", "work"),
        string.Concat("Mon", "go")
    ];

    /// <summary>Roots that ship or build the product. History under docs/, specs/ and archives is out of scope.</summary>
    private static readonly string[] ScannedRoots = ["src", "tests", "tools", "docker", "e2e-tests", ".github", ".config"];

    /// <summary>Repository-level files that select packages and feeds.</summary>
    private static readonly string[] ScannedFiles = ["Directory.Packages.props", "NuGet.config", "Elsa.Server.slnx"];

    private static readonly string[] ScannedExtensions =
    [
        ".cs", ".csproj", ".props", ".targets", ".json", ".slnx", ".slnf", ".yml", ".yaml",
        ".config", ".sh", ".ps1", ".py", ".sql", ".Dockerfile"
    ];

    [Fact]
    public void No_project_package_source_or_configuration_names_a_retired_persistence_family()
    {
        var scanned = 0;
        var violations = new List<string>();

        foreach (var (path, relativePath) in ScannedSourceFiles())
        {
            scanned++;
            foreach (var name in RetiredNames)
            {
                if (File.ReadAllText(path).Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}: names the retired '{name}' persistence family");
                    break;
                }
            }
        }

        Assert.True(scanned > 0, "The retired-family guard scanned no files; its roots or extensions are wrong.");
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
                         .Where(path => !IsBuildOutput(path)))
            {
                scannedProjects++;
                var relativePath = RelativePath(project);
                if (RetiredNames.Any(name => relativePath.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    violations.Add($"{relativePath}: project path names a retired persistence family");
            }

            foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOutput(path)))
            {
                var relativePath = RelativePath(directory);
                if (RetiredNames.Any(name => Path.GetFileName(relativePath).Contains(name, StringComparison.OrdinalIgnoreCase)))
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
            Assert.DoesNotContain(RetiredNames, name =>
                relativePath.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                File.ReadAllText(filter).Contains(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Mutation proof. Each shape the guard scans — file content, project path, directory name — is fed a
    /// synthetic violation, and the same predicates that back the guards above must flag it. Without this
    /// a scan that silently matched nothing would read as a pass.
    /// </summary>
    [Fact]
    public void Guard_detects_a_synthetic_reference_in_each_scanned_shape()
    {
        var retired = RetiredNames[0];
        var content = $"using {retired}.Kernel;";
        var projectPath = $"src/essentials/Persistence/{retired}/Elsa.Persistence.{retired}.csproj";
        var directoryName = retired;
        var package = $"{retired}.Sqlite";

        Assert.Contains(RetiredNames, name => content.Contains(name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(RetiredNames, name => projectPath.Contains(name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(RetiredNames, name => directoryName.Contains(name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(RetiredNames, name => package.Contains(name, StringComparison.OrdinalIgnoreCase));

        // A clean input must not trip the same predicates, so the guard cannot pass by matching everything.
        const string clean = "using Elsa.Persistence.EntityFramework;";
        Assert.DoesNotContain(RetiredNames, name => clean.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<(string Path, string RelativePath)> ScannedSourceFiles()
    {
        foreach (var root in ScannedRoots)
        {
            var rootPath = FullPath(root);
            if (!Directory.Exists(rootPath))
                continue;

            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(path) || !ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    continue;
                yield return (path, RelativePath(path));
            }
        }

        foreach (var file in ScannedFiles)
        {
            var path = FullPath(file);
            if (File.Exists(path))
                yield return (path, RelativePath(path));
        }
    }

    private static bool IsBuildOutput(string path) =>
        BuildOutputRegex().IsMatch(path.Replace(Path.DirectorySeparatorChar, '/'));

    [GeneratedRegex(@"/(bin|obj|node_modules)/", RegexOptions.CultureInvariant)]
    private static partial Regex BuildOutputRegex();

    private static string FullPath(string relativePath) => Path.Combine(RepoRoot, relativePath);

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
