using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.ExtensionBoundary.Tests;

/// <summary>
/// Issue #1815 splits the tree into a required core under <c>src/</c> and optional modules under
/// <c>extensions/</c>. The split is only worth anything if the direction of dependency is enforced, so
/// this guard fails when core reaches into an extension, and when one extension reaches into another
/// without that edge having been declared.
/// </summary>
/// <remarks>
/// It reads <c>ProjectReference</c> includes out of the <c>.csproj</c> files themselves rather than
/// inspecting compiled assemblies. That keeps the suite reference-free, which is what lets it live
/// inside the core-only solution filter, and it means a violation is named at its source line rather
/// than inferred from a load failure.
/// <para>
/// Transitivity needs no separate check: if no core project references an extension directly, and no
/// extension is part of core, then no path from core to an extension exists at all.
/// </para>
/// <para>
/// Only production projects are examined. Test projects legitimately cross the boundary in both
/// directions, and <c>Elsa.Architecture.Tests</c> in particular references several optional modules
/// on purpose.
/// </para>
/// </remarks>
public sealed class ExtensionBoundaryTests
{
    private const string ExtensionsRoot = "extensions";
    private const string CoreRoot = "src";

    /// <summary>
    /// Extension-to-extension edges that have been reviewed and accepted, as
    /// <c>referencing project -> referenced project</c>.
    /// </summary>
    /// <remarks>
    /// Empty while <c>extensions/</c> is empty. Every entry added later needs a comment saying why the
    /// edge is legitimate, in the same spirit as <c>EfCoreDependencyGuardTests.AllowedEfConsumers</c>:
    /// the point is that the list is short and argued, not that it is absent.
    /// </remarks>
    private static readonly (string From, string To)[] DeclaredExtensionEdges = [];

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Core_projects_never_reference_an_extension()
    {
        var offenders = ReadModuleProjects()
            .Where(project => !project.IsExtension)
            .SelectMany(project => project.References
                .Where(reference => IsUnder(reference.Path, ExtensionsRoot))
                .Select(reference => $"{project.Name} -> {reference.Name} ({reference.Path})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, Report(
            "Core projects reference an extension. Core must not depend on an optional module: either the "
            + "referenced project belongs in core, or the dependency belongs behind a contract core already owns.",
            offenders));
    }

    [Fact]
    public void Extension_to_extension_references_are_declared()
    {
        var declared = DeclaredExtensionEdges.ToHashSet();

        var offenders = ReadModuleProjects()
            .Where(project => project.IsExtension)
            .SelectMany(project => project.References
                .Where(reference => IsUnder(reference.Path, ExtensionsRoot))
                .Where(reference => !string.Equals(reference.Bucket, project.Bucket, StringComparison.Ordinal))
                .Where(reference => !declared.Contains((project.Name, reference.Name)))
                .Select(reference => $"{project.Name} [{project.Bucket}] -> {reference.Name} [{reference.Bucket}]"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, Report(
            "Undeclared extension-to-extension references. Add the edge to DeclaredExtensionEdges with a "
            + "comment justifying it, or move the projects into the same extension bucket.",
            offenders));
    }

    /// <summary>
    /// Without this, both guards above would pass vacuously the day someone moves every optional module
    /// back under <c>src/</c>: no extensions means no edges to find.
    /// </summary>
    [Fact]
    public void Extensions_root_exists_and_documents_itself()
    {
        var root = Path.Join(RepoRoot, ExtensionsRoot);

        Assert.True(Directory.Exists(root), $"'{ExtensionsRoot}/' is missing. The boundary guards cannot mean anything without it.");
        Assert.True(File.Exists(Path.Join(root, "README.md")), $"'{ExtensionsRoot}/README.md' is missing; it carries the layout rules.");
    }

    /// <summary>
    /// Every declared edge must still exist. A stale entry would keep permitting an edge nobody has, and
    /// would quietly pre-authorize its reintroduction.
    /// </summary>
    [Fact]
    public void Declared_extension_edges_are_all_still_present()
    {
        var actual = ReadModuleProjects()
            .Where(project => project.IsExtension)
            .SelectMany(project => project.References.Select(reference => (project.Name, reference.Name)))
            .ToHashSet();

        var stale = DeclaredExtensionEdges
            .Where(edge => !actual.Contains(edge))
            .Select(edge => $"{edge.From} -> {edge.To}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0, Report("Declared extension edges that no longer exist; remove them.", stale));
    }

    private static IReadOnlyList<ModuleProject> ReadModuleProjects()
    {
        var projects = new List<ModuleProject>();

        foreach (var root in new[] { CoreRoot, ExtensionsRoot })
        {
            var absoluteRoot = Path.Join(RepoRoot, root);
            if (!Directory.Exists(absoluteRoot)) continue;

            foreach (var absolutePath in Directory.EnumerateFiles(absoluteRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var path = Relative(absolutePath);
                if (IsTestPath(path)) continue;

                projects.Add(new ModuleProject(
                    Path.GetFileNameWithoutExtension(path),
                    path,
                    root == ExtensionsRoot,
                    Bucket(path),
                    ReadReferences(absolutePath)));
            }
        }

        return projects;
    }

    private static IReadOnlyList<ProjectReference> ReadReferences(string absoluteProjectPath)
    {
        var directory = Path.GetDirectoryName(absoluteProjectPath)!;

        return XDocument.Load(absoluteProjectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(include!.Replace('\\', Path.DirectorySeparatorChar), directory))
            .Select(Relative)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectReference(Path.GetFileNameWithoutExtension(path), path, Bucket(path)))
            .Order()
            .ToArray();
    }

    /// <summary>The extension a path belongs to, i.e. the segment right under <c>extensions/</c>.</summary>
    private static string Bucket(string relativePath) =>
        IsUnder(relativePath, ExtensionsRoot) && relativePath.Split('/') is { Length: > 1 } segments
            ? segments[1]
            : string.Empty;

    private static bool IsUnder(string relativePath, string root) =>
        relativePath.StartsWith(root + "/", StringComparison.Ordinal);

    private static bool IsTestPath(string relativePath) =>
        relativePath.StartsWith("tests/", StringComparison.Ordinal) ||
        relativePath.Contains("/tests/", StringComparison.Ordinal);

    private static string Relative(string absolutePath) =>
        Path.GetRelativePath(RepoRoot, absolutePath).Replace('\\', '/');

    private static string Report(string headline, IReadOnlyList<string> offenders) =>
        offenders.Count == 0
            ? string.Empty
            : $"{headline}{Environment.NewLine}{Environment.NewLine}"
              + string.Join(Environment.NewLine, offenders.Select(offender => "  " + offender));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record ModuleProject(
        string Name,
        string Path,
        bool IsExtension,
        string Bucket,
        IReadOnlyList<ProjectReference> References);

    private sealed record ProjectReference(string Name, string Path, string Bucket) : IComparable<ProjectReference>
    {
        public int CompareTo(ProjectReference? other) => string.CompareOrdinal(Path, other?.Path);
    }
}
