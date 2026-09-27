using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>
/// The dependency map's pinned-transitive edges (spec 149 FR-012): for each packable project, the packages central
/// package management pins for it only because it reaches them through its other dependencies.
/// </summary>
/// <remarks>
/// <para>
/// With <c>CentralPackageTransitivePinningEnabled</c>, restore lists every such package, project references included,
/// under <c>centralTransitiveDependencyGroups</c> in the project's <c>obj/project.assets.json</c>, and pack writes exactly
/// that list into the nuspec, at the pinned version. So the edges are read from the list pack itself reads, not
/// recomputed from package metadata, where a second resolver could disagree with NuGet's.
/// </para>
/// <para>
/// Restore output is not part of the tree, so each file is held to the tree before it is read: a restore made before
/// the project's pins, package references or project references last changed is refused, not read. The maps check
/// restores first, so what it compares against the committed dataset is never stale.
/// </para>
/// </remarks>
public static class PinnedTransitiveDependencies
{
    private const string Restore = "dotnet restore Elsa.Server.slnx";

    /// <summary>The projects, each packable one with its pinned-transitive edges after its direct ones.</summary>
    public static IReadOnlyList<ProjectFacts> Attach(RepoContext repo, IReadOnlyList<ProjectFacts> projects)
    {
        var packages = PackageVersions.Load(repo);
        return projects
            .Select(project => project.Packable ? project with { Edges = [.. project.Edges, .. Read(repo, project, packages.For(project.Name))] } : project)
            .ToArray();
    }

    private static PinnedTransitiveEdge[] Read(RepoContext repo, ProjectFacts project, IReadOnlyDictionary<string, string> pins)
    {
        var directory = project.RelativePath[..(project.RelativePath.LastIndexOf('/') + 1)];
        var assetsPath = directory + "obj/project.assets.json";
        if (!File.Exists(repo.Absolute(assetsPath)))
            throw Unreadable(project, $"it has no restore output ({assetsPath})");

        using var assets = JsonDocument.Parse(File.ReadAllBytes(repo.Absolute(assetsPath)));
        var restored = assets.RootElement.GetProperty("project");
        var frameworks = Properties(restored, "frameworks").Select(framework => framework.Value).ToArray();

        var restoredPins = frameworks.Select(framework => Properties(framework, "centralPackageVersions")
            .ToDictionary(pin => pin.Name, pin => pin.Value.GetString(), StringComparer.OrdinalIgnoreCase));
        if (restoredPins.Any(versions => versions.Count != pins.Count || pins.Any(pin => versions.GetValueOrDefault(pin.Key) != pin.Value)))
            throw Unreadable(project, $"{assetsPath} was restored with other pins than Directory.Packages.props holds");

        var restoredPackages = frameworks.SelectMany(framework => Properties(framework, "dependencies")).Select(dependency => dependency.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (project.Packages.FirstOrDefault(package => !restoredPackages.Contains(package.Id)) is { } unrestored)
            throw Unreadable(project, $"{assetsPath} was restored before it referenced {unrestored.Id}");

        // Restore records project references by absolute path; relative to the project they are independent of where
        // the checkout was when it restored.
        var restoredFrom = Path.GetDirectoryName(restored.GetProperty("restore").GetProperty("projectPath").GetString())!;
        var restoredReferences = Properties(restored.GetProperty("restore"), "frameworks")
            .SelectMany(framework => Properties(framework.Value, "projectReferences"))
            .Select(reference => Path.GetRelativePath(repo.Root, Path.GetFullPath(Path.GetRelativePath(restoredFrom, reference.Name), repo.Absolute(directory))).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!restoredReferences.SetEquals(project.Edges.OfType<InternalEdge>().Select(edge => edge.RelativePath)))
            throw Unreadable(project, $"{assetsPath} was restored with other project references than {project.RelativePath} has");

        return Properties(assets.RootElement, "centralTransitiveDependencyGroups")
            .SelectMany(group => group.Value.EnumerateObject())
            .Select(package => (package.Name, Range: package.Value.GetProperty("version").GetString()))
            .Select(package => pins.TryGetValue(package.Name, out var pin) && package.Range == $"[{pin}, )"
                ? new PinnedTransitiveEdge(package.Name, pin)
                : throw Unreadable(project, $"{assetsPath} pins {package.Name} at {package.Range}, which is not the version Directory.Packages.props pins"))
            .Distinct()
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ThenBy(edge => edge.Version, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A JSON object's properties, or none when it lacks the object, as restore omits an empty one.</summary>
    private static IEnumerable<JsonProperty> Properties(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.EnumerateObject() : [];

    private static InvalidOperationException Unreadable(ProjectFacts project, string reason) =>
        new($"{project.RelativePath}: {reason}. The dependency map reads the packages NuGet pins for a project transitively " +
            $"from its restore output (spec 149 FR-012); restore, then generate again: {Restore}");
}
