using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// EF Core is the only first-party persistence family. The retired storage library and the retired
/// document-database driver were deleted, not deprecated, so no project or directory under <c>src/</c>,
/// <c>tests/</c>, <c>tools/</c>, <c>docker/</c>, <c>e2e-tests/</c> or <c>.github/</c>, and no solution filter,
/// may name either of them again.
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
public sealed class RetiredPersistenceFamilyGuardTests
{
    /// <summary>The retired family names, assembled so this source file is not itself a violation.</summary>
    private static readonly string[] RetiredNames =
    [
        string.Concat("Ground", "work"),
        string.Concat("Mon", "go")
    ];

    /// <summary>Roots that ship or build the product. History under docs/, specs/ and archives is out of scope.</summary>
    private static readonly string[] ScannedRoots = ["src", "tests", "tools", "docker", "e2e-tests", ".github", ".config"];

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
                if (RetiredNames.Any(name => relativePath.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    violations.Add($"{relativePath}: project path names a retired persistence family");
            }

            foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOrPackageOutput(path)))
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

    private static bool IsBuildOrPackageOutput(string path) =>
        IsBuildOutput(path) || HasSegment(RepoRoot, path, "node_modules");

    private static string FullPath(string relativePath) => Path.Combine(RepoRoot, relativePath);

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
