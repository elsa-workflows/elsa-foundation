using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// NuGet lock files (#2122, docs/reference/nuget-lock-files.md): every project in <c>Elsa.Server.slnx</c> restores
/// against a committed <c>packages.lock.json</c>, and the dependency map's pinned-transitive edges, which the maps
/// generator reads from those files, still name exactly what pack writes into each nuspec.
/// </summary>
public sealed class NuGetLockFileTests
{
    private const string LockFileName = "packages.lock.json";

    /// <summary>
    /// CI restores in locked mode, which fails when a committed lock file no longer matches its project, but not when
    /// there is none: NuGet then writes one and carries on. A project added without its lock file would restore in CI
    /// resolved afresh while every restore step reads as locked, so its absence fails here instead. Two projects in one
    /// directory would share one lock file, and a lock file with no project beside it pins nothing.
    /// </summary>
    [Fact]
    public void Every_solution_project_has_a_committed_lock_file_and_every_lock_file_a_solution_project()
    {
        var projects = XDocument.Load(Path.Join(RepoRoot, "Elsa.Server.slnx")).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value.Replace('\\', '/'))
            .ToArray();
        var (exitCode, output) = ChildProcess.Run("git", ["ls-files", "-z", "--", $"*{LockFileName}"], RepoRoot);
        Assert.True(exitCode == 0, $"git ls-files failed with exit {exitCode}:\n{output}");
        var committed = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var expected = projects.Select(project => DirectoryOf(project) + LockFileName).ToHashSet(StringComparer.Ordinal);

        var violations = projects.GroupBy(DirectoryOf, StringComparer.Ordinal).Where(directory => directory.Count() > 1)
            .Select(directory => $"{string.Join(" and ", directory)} share {directory.Key}{LockFileName}; give one its own NuGetLockFilePath")
            .Concat(projects.Where(project => !committed.Contains(DirectoryOf(project) + LockFileName))
                .Select(project => $"{project} has no committed {DirectoryOf(project)}{LockFileName}; restore, then commit it"))
            .Concat(committed.Where(lockFile => !expected.Contains(lockFile))
                .Select(lockFile => $"{lockFile} sits beside no project in Elsa.Server.slnx"))
            .ToArray();

        Assert.True(projects.Length > 0, "Elsa.Server.slnx lists no projects, so this guard would check nothing.");
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The maps generator reads a packable project's pinned-transitive edges from its lock file (spec 149 FR-012), but
    /// pack writes the nuspec from restore's <c>centralTransitiveDependencyGroups</c>. The two lists differ wherever a
    /// pinned package is reached only through a <c>PrivateAssets="all"</c> reference, which the generator reads from the
    /// project file to bridge. A reading that drifted from NuGet's would regenerate the same wrong map the maps check
    /// compares with, and pass there, so this holds every committed edge to NuGet's own list, from the restore CI runs
    /// before this suite.
    /// </summary>
    [Fact]
    public void Every_packable_nodes_pinned_transitive_edges_are_the_packages_restore_lists_for_its_nuspec()
    {
        using var map = JsonDocument.Parse(File.ReadAllBytes(Path.Join(RepoRoot, "docs", "maps", "dependency-map.json")));
        var packable = map.RootElement.GetProperty("nodes").EnumerateArray().Where(node => node.GetProperty("packable").GetBoolean()).ToArray();

        var violations = packable
            .Select(node => (
                Path: node.GetProperty("path").GetString()!,
                Recorded: node.GetProperty("edges").EnumerateArray()
                    .Where(edge => edge.GetProperty("type").GetString() == "pinned-transitive")
                    .Select(edge => $"{edge.GetProperty("id").GetString()} {edge.GetProperty("version").GetString()}")
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .Select(node => (node.Path, node.Recorded, Restored: RestoredPins(node.Path)))
            .Where(node => !node.Recorded.SequenceEqual(node.Restored))
            .Select(node => $"{node.Path}: the map records [{string.Join(", ", node.Recorded)}], restore lists [{string.Join(", ", node.Restored)}]")
            .ToArray();

        Assert.NotEmpty(packable);
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Each package restore lists for pack beyond the project's direct dependencies, with the version its range starts at.</summary>
    private static string[] RestoredPins(string projectPath)
    {
        var assets = Path.Join(RepoRoot, DirectoryOf(projectPath), "obj", "project.assets.json");
        if (!File.Exists(assets))
            throw new InvalidOperationException(
                $"{projectPath} has no restore output ({assets}). Restore first: bash tools/architecture/restore-ci-project-graph.sh");

        using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
        return document.RootElement.TryGetProperty("centralTransitiveDependencyGroups", out var groups)
            ? groups.EnumerateObject().SelectMany(group => group.Value.EnumerateObject())
                .Select(package => (package.Name, Range: package.Value.GetProperty("version").GetString()!))
                .Select(package => $"{package.Name} {(package.Range.StartsWith('[') && package.Range.EndsWith(", )", StringComparison.Ordinal) ? package.Range[1..^3] : package.Range)}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    /// <summary>A repo-relative project path's directory, with its trailing slash.</summary>
    private static string DirectoryOf(string projectPath) => projectPath[..(projectPath.LastIndexOf('/') + 1)];
}
