using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>
/// The dependency map's pinned-transitive edges (spec 149 FR-012): for each packable project, the packages central
/// package management pins for it only because it reaches them through its other dependencies, read from the NuGet lock
/// file committed beside the project.
/// </summary>
/// <remarks>
/// <para>
/// With <c>CentralPackageTransitivePinningEnabled</c>, pack writes into the nuspec, at the pinned version, every pinned
/// package the project reaches but does not reference, unless each direct reference it is reached through declares
/// <c>PrivateAssets="all"</c>: the list restore writes under <c>centralTransitiveDependencyGroups</c> in
/// <c>obj/project.assets.json</c>, which pack reads. The lock file, <c>packages.lock.json</c>, types every pinned package
/// the project reaches but does not reference <c>CentralTransitive</c>, the privately reached ones included, and lists
/// each package's and project's own dependencies. So the edges are the <c>CentralTransitive</c> entries reachable, over
/// those lists, from a direct reference that is not private; a package reached only privately is left out, as pack
/// leaves it out. Reading the committed file keeps generation a scan of the tree, with no restore. A direct reference
/// only a <c>Directory.Build.props</c> adds is not in the project file, so it counts as not private; the Architecture
/// suite's <c>NuGetLockFileTests</c> holds every committed edge to restore's own list once CI has restored, so a shape
/// where this reading and NuGet's disagree fails there instead of reaching the calculator.
/// </para>
/// <para>
/// A lock file the tree has moved past is refused, not read, on the rules NuGet's locked mode applies: one missing a
/// package the project references, locking one at another version, pinning one at another version than
/// <c>Directory.Packages.props</c> holds, leaving unpinned one that file now pins, or listing other projects than the
/// project's references reach. CI restores in locked mode, so such a file fails there too.
/// </para>
/// </remarks>
public static class PinnedTransitiveDependencies
{
    /// <summary>The lock file's name beside each project.</summary>
    public const string LockFileName = "packages.lock.json";

    /// <summary>What rewrites the lock files once the tree's dependencies change (docs/reference/nuget-lock-files.md).</summary>
    private const string UpdateLockFiles = "dotnet restore Elsa.Server.slnx";

    /// <summary>The projects, each packable one with its pinned-transitive edges after its direct ones.</summary>
    public static IReadOnlyList<ProjectFacts> Attach(RepoContext repo, IReadOnlyList<ProjectFacts> projects)
    {
        var packages = PackageVersions.Load(repo);
        return projects
            .Select(project => project.Packable ? project with { Edges = [.. project.Edges, .. Read(repo, project, projects, packages)] } : project)
            .ToArray();
    }

    /// <summary>One project's pinned-transitive edges, with <paramref name="graph"/> the projects its references resolve in.</summary>
    internal static PinnedTransitiveEdge[] Read(RepoContext repo, ProjectFacts project, IReadOnlyList<ProjectFacts> graph, PackageVersions packages)
    {
        var lockPath = project.RelativePath[..(project.RelativePath.LastIndexOf('/') + 1)] + LockFileName;
        if (!File.Exists(repo.Absolute(lockPath)))
            throw Unreadable(project, $"it has no NuGet lock file ({lockPath})");

        using var lockFile = JsonDocument.Parse(File.ReadAllBytes(repo.Absolute(lockPath)));
        if (!lockFile.RootElement.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 2)
            throw Unreadable(project, $"{lockPath} is not a version 2 lock file, the version central package management writes");

        // A runtime-specific target ("net10.0/linux-x64") lists only what that runtime adds, and pack reads no runtime graph.
        var targets = Properties(lockFile.RootElement, "dependencies")
            .Where(target => !target.Name.Contains('/', StringComparison.Ordinal))
            .Select(target => target.Value.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (targets.Length == 0)
            throw Unreadable(project, $"{lockPath} locks no target framework");

        var pins = packages.For(project.Name);
        var pinIds = pins.Keys.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        var projects = ProjectClosure(project, graph);

        return targets
            .SelectMany(entries => PinnedTransitive(project, lockPath, entries, pins, projects))
            .Select(id => new PinnedTransitiveEdge(pinIds[id], pins[id]))
            .Distinct()
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ThenBy(edge => edge.Version, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>One target's pinned-transitive package ids, once the target is held to the tree.</summary>
    private static IEnumerable<string> PinnedTransitive(
        ProjectFacts project, string lockPath, IReadOnlyDictionary<string, JsonElement> entries, IReadOnlyDictionary<string, string> pins, IReadOnlySet<string> projects)
    {
        foreach (var package in project.Packages)
        {
            if (!entries.TryGetValue(package.Id, out var entry) || Type(entry) != "Direct")
                throw Unreadable(project, $"{lockPath} was written before it referenced {package.Id}");
            if (package.Version is not null && Requested(entry) != Range(package.Version))
                throw Unreadable(project, $"{lockPath} locks {package.Id} at {Requested(entry)}, not at the version it references, {package.Version}");
        }

        foreach (var (id, entry) in entries)
        {
            if (Type(entry) == "CentralTransitive" && (!pins.TryGetValue(id, out var pin) || Requested(entry) != Range(pin)))
                throw Unreadable(project, $"{lockPath} pins {id} at {Requested(entry)}, which is not the version Directory.Packages.props pins");
            if (Type(entry) == "Transitive" && pins.ContainsKey(id))
                throw Unreadable(project, $"{lockPath} leaves {id} unpinned, but Directory.Packages.props pins it");
        }

        if (!entries.Where(entry => Type(entry.Value) == "Project").Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(projects))
            throw Unreadable(project, $"{lockPath} lists other projects than the references of {project.RelativePath} reach");

        // Everything the project's references that are not private bring in: packages and projects alike list their own
        // dependencies, by id, in the lock file.
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(entries
            .Where(entry => Type(entry.Value) == "Direct").Select(entry => entry.Key)
            .Concat(project.Edges.OfType<InternalEdge>().Select(edge => edge.Id))
            .Where(id => !project.PrivateReferences.Contains(id)));
        while (pending.TryPop(out var id))
        {
            if (reached.Add(id) && entries.TryGetValue(id, out var entry))
                foreach (var dependency in Properties(entry, "dependencies"))
                    pending.Push(dependency.Name);
        }

        return entries.Where(entry => Type(entry.Value) == "CentralTransitive" && reached.Contains(entry.Key)).Select(entry => entry.Key);
    }

    /// <summary>The package identities of every project the project's references reach, as the lock file keys them.</summary>
    private static HashSet<string> ProjectClosure(ProjectFacts project, IReadOnlyList<ProjectFacts> graph)
    {
        var byPath = graph.ToDictionary(node => node.RelativePath, StringComparer.Ordinal);
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<InternalEdge>(project.Edges.OfType<InternalEdge>());
        while (pending.TryPop(out var edge))
        {
            if (!visited.Add(edge.RelativePath))
                continue;

            closure.Add(edge.Id);
            foreach (var next in byPath.GetValueOrDefault(edge.RelativePath)?.Edges.OfType<InternalEdge>() ?? [])
                pending.Push(next);
        }

        return closure;
    }

    /// <summary>The range restore records for a plain version: <c>[version, )</c>.</summary>
    private static string Range(string version) => $"[{version}, )";

    private static string? Type(JsonElement entry) => entry.TryGetProperty("type", out var type) ? type.GetString() : null;

    private static string? Requested(JsonElement entry) => entry.TryGetProperty("requested", out var requested) ? requested.GetString() : null;

    /// <summary>A JSON object's properties, or none when it lacks the object, as NuGet omits an empty one.</summary>
    private static IEnumerable<JsonProperty> Properties(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.EnumerateObject() : [];

    private static InvalidOperationException Unreadable(ProjectFacts project, string reason) =>
        new($"{project.RelativePath}: {reason}. The dependency map reads the packages NuGet pins for a project transitively " +
            $"from its committed lock file (spec 149 FR-012); update the lock files, review and commit them, then generate again: {UpdateLockFiles}");
}
