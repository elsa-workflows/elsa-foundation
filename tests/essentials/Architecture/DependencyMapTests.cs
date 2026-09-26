using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Holds the committed dependency map (<c>docs/maps/dependency-map.json</c>, spec 149) to what its consumers rely
/// on, reading the JSON alone as they do and never the generator that writes it (SC-005).
/// </summary>
/// <remarks>
/// Freshness is the maps check's concern: it byte-compares the committed dataset with a regeneration. This class
/// pins that the dataset answers correctly: ownership by the longest matching project directory (FR-006, SC-001),
/// and a package id and version line on every packable node (FR-003, FR-004).
/// </remarks>
public sealed class DependencyMapTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string RepoRoot { get; } = FindRepoRoot();

    private static IReadOnlyList<Node> Nodes { get; } = ReadNodes();

    /// <summary>
    /// SC-001: for every tracked file under <c>src/</c> and <c>tests/</c>, the owner resolved from the dataset alone
    /// is the project whose directory is nearest above the file on disk. The oracle walks the filesystem and never
    /// reads the dataset, so a node missing from it, or a resolver that is not longest-prefix, disagrees.
    /// </summary>
    [Fact]
    public void Every_tracked_file_resolves_to_the_nearest_project_on_disk()
    {
        var files = TrackedFiles("src", "tests");
        var projectsOnDisk = new Dictionary<string, string?>(StringComparer.Ordinal);
        var owners = files.Select(file => (File: file, Owner: OwningProject(file))).ToArray();

        var mismatches = owners
            .Select(owned => (owned.File, FromMap: owned.Owner?.Path, OnDisk: NearestProjectOnDisk(owned.File, projectsOnDisk)))
            .Where(owned => owned.FromMap != owned.OnDisk)
            .Select(owned => $"{owned.File}: dataset says {owned.FromMap ?? "no owner"}, disk says {owned.OnDisk ?? "no owner"}")
            .ToArray();

        Assert.True(mismatches.Length == 0,
            $"{mismatches.Length} of {files.Count} tracked files resolve to a different owner from the dataset than on disk:" +
            string.Concat(mismatches.Take(20).Select(mismatch => $"{Environment.NewLine}  {mismatch}")));

        // Not vacuous: the sweep covers files owned by a project nested inside another, and files no project owns.
        Assert.Contains(owners, owned => owned.Owner is { } owner && Nodes.Any(outer => outer != owner && owner.Directory.StartsWith(outer.Directory, StringComparison.Ordinal)));
        Assert.Contains(owners, owned => owned.Owner is null);
    }

    /// <summary>FR-006 on paths that need no file to exist: the deeper project owns its subtree, and nothing is guessed.</summary>
    [Theory]
    [InlineData("src/essentials/Workflows/Runtime/Core/Services/Foo.cs", "Elsa.Workflows.Runtime.Core")]
    [InlineData("src/essentials/Workflows/Runtime/Services/Foo.cs", "Elsa.Workflows.Runtime")]
    [InlineData("src/essentials/Workflows/RuntimeExtras/Foo.cs", null)]
    [InlineData("src/essentials/Workflows/Foo.cs", null)]
    [InlineData("tests/Directory.Build.props", null)]
    public void A_path_is_owned_by_the_longest_matching_project_directory(string path, string? expected) =>
        Assert.Equal(expected, OwningProject(path)?.Name);

    [Fact]
    public void Every_packable_node_carries_a_package_id_and_a_version_line()
    {
        Assert.Contains(Nodes, node => node.Packable);

        // A test project never publishes, so it carries neither; that is what lets a non-packable node omit both.
        var violations = Nodes
            .Where(node => node.Packable
                ? node.Kind != "source" || string.IsNullOrEmpty(node.PackageId) || node.Line is not ("A" or "B")
                : node.PackageId is not null || node.Line is not null)
            .Select(node => $"{node.Path}: packable {node.Packable}, kind {node.Kind}, package id {node.PackageId ?? "null"}, line {node.Line ?? "null"}")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// FR-004: Line A is exactly the reviewed list, matched by whole name as <c>Directory.Build.props</c> matches it,
    /// so a project whose name merely extends a member's (Elsa.Tasks beside Elsa.Tasks.Core) stays Line B.
    /// </summary>
    [Fact]
    public void Line_a_nodes_are_exactly_the_reviewed_list()
    {
        Assert.Equal(
            VersionLines.LineAMembers(RepoRoot).Order(StringComparer.Ordinal),
            Nodes.Where(node => node.Line == "A").Select(node => node.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>FR-006 as a consumer implements it: the node whose directory is the longest prefix of the path.</summary>
    private static Node? OwningProject(string relativePath) =>
        Nodes.Where(node => relativePath.StartsWith(node.Directory, StringComparison.Ordinal)).MaxBy(node => node.Directory.Length);

    /// <summary>The first <c>.csproj</c> found walking up from the file's directory, read from disk.</summary>
    private static string? NearestProjectOnDisk(string relativePath, Dictionary<string, string?> projectsOnDisk)
    {
        var separator = relativePath.LastIndexOf('/');
        return separator < 0 ? null : ProjectOnDisk(relativePath[..separator], projectsOnDisk);
    }

    private static string? ProjectOnDisk(string directory, Dictionary<string, string?> projectsOnDisk)
    {
        if (projectsOnDisk.TryGetValue(directory, out var cached))
            return cached;

        var absolute = Path.Join(RepoRoot, directory);
        var project = Directory.Exists(absolute)
            ? Directory.EnumerateFiles(absolute, "*.csproj").Select(Path.GetFileName).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

        return projectsOnDisk[directory] = project is not null
            ? $"{directory}/{project}"
            : NearestProjectOnDisk(directory, projectsOnDisk);
    }

    private static IReadOnlyList<string> TrackedFiles(params string[] roots)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = RepoRoot, RedirectStandardOutput = true };
        foreach (var argument in (string[])["ls-files", "-z", "--", .. roots])
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Reads the dataset, checking the schema version at read time as every consumer should.</summary>
    private static IReadOnlyList<Node> ReadNodes()
    {
        var dataset = JsonSerializer.Deserialize<Dataset>(File.ReadAllText(Path.Join(RepoRoot, "docs", "maps", "dependency-map.json")), JsonOptions)
                      ?? throw new InvalidOperationException("docs/maps/dependency-map.json is empty.");

        return dataset.SchemaVersion == 1
            ? dataset.Nodes
            : throw new InvalidOperationException($"docs/maps/dependency-map.json has schema version {dataset.SchemaVersion}; these tests read version 1.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record Dataset(int SchemaVersion, IReadOnlyList<Node> Nodes);

    private sealed record Node(string Path, string Name, string Kind, bool Packable, string? PackageId, string? Line)
    {
        /// <summary>The project's directory with its trailing slash, so a sibling sharing a name prefix never matches.</summary>
        public string Directory { get; } = Path[..(Path.LastIndexOf('/') + 1)];
    }
}
