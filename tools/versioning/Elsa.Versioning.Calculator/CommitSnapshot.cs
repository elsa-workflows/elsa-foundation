using System.Text;
using Elsa.Maps.Projects;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// One commit as change detection sees it: its tree, the dependency map for it, and from those, the package-affecting
/// inputs of any package at that commit.
/// </summary>
/// <remarks>
/// <para>
/// Everything comes from the commit's own objects, including the dependency map, so ownership is resolved "from the
/// dependency map at each of the two commits" (spec 150 FR-002) and a package that moved is found at its old path in
/// the old commit and its new path in the new one. Nothing is keyed on a path across commits except the inputs'
/// own keys, whose difference is exactly what a move is.
/// </para>
/// <para>
/// The dependency map is the copy committed in the commit when it has one, and otherwise derived from the commit's tree
/// by the scan the maps generator runs (<see cref="DependencyMapJson"/>, spec 259). Commits from before the map stopped
/// being committed keep their committed bytes, so the inputs recorded for their publishes never move; every later
/// commit is derived. <see cref="DatasetSource"/> can force either source, to compare them.
/// </para>
/// <para>A package's inputs are:</para>
/// <list type="bullet">
/// <item>every file its project owns — the longest matching project directory (spec 149 FR-006) — except a Markdown
/// document none of its build files names (FR-002a); for a project that packs as a tool, every file of every project it
/// references too, since a tool carries its references' builds inside its own package;</item>
/// <item>the build files every build of it reads: each <c>Directory.Build.props</c>, <c>Directory.Build.targets</c>,
/// <c>Directory.Build.rsp</c>, <c>NuGet.config</c>, <c>global.json</c> and non-root <c>Directory.Packages.props</c> in
/// its directory's ancestors, and every file those and its own MSBuild files import (FR-004);</item>
/// <item>for each external package its nuspec lists — each it references directly, and each central package management
/// pins for it transitively (spec 149's pinned-transitive edges) — that package's <c>Directory.Packages.props</c>
/// entries (FR-003), and everything in that file other than <c>PackageVersion</c> entries as one input (FR-004,
/// <see cref="CentralPackages"/>).</item>
/// </list>
/// </remarks>
internal sealed class CommitSnapshot
{
    /// <summary>Files MSBuild, NuGet or the SDK read from every directory above a project, by lowercase name.</summary>
    private static readonly IReadOnlySet<string> AmbientNames = new HashSet<string>(
        ["directory.build.props", "directory.build.targets", "directory.build.rsp", "directory.packages.props", "nuget.config", "global.json"],
        StringComparer.Ordinal);

    private readonly ObjectCache objects;
    private readonly Dictionary<string, TreeEntry> tree;
    private readonly Dictionary<string, List<TreeEntry>> byDirectory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TreeEntry>> owned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackageInputs> inputs = new(StringComparer.Ordinal);
    private readonly Lazy<CentralPackages> central;

    public CommitSnapshot(ObjectCache cache, string commit, DatasetSource source = DatasetSource.Auto)
    {
        objects = cache;
        Commit = commit;
        tree = objects.Git.ListTree(commit).ToDictionary(entry => entry.Path, StringComparer.Ordinal);

        var committed = tree.GetValueOrDefault(DependencyMapDataset.RelativePath);
        MapDerived = source == DatasetSource.Derived || (source == DatasetSource.Auto && committed is null);
        Map = MapDerived
            ? DependencyMapDataset.Parse(Derive(), $"the dependency map derived at {commit}")
            : committed is not null
                ? DependencyMapDataset.Parse(Encoding.UTF8.GetString(objects.Read(committed.ObjectId)), $"{DependencyMapDataset.RelativePath} at {commit}")
                : throw new InvalidOperationException(
                    $"Commit {commit} has no {DependencyMapDataset.RelativePath}; use --dataset auto or derived.");

        var projectDirectories = Map.Nodes.GroupBy(node => node.Directory, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var entry in tree.Values)
        {
            var directory = RepositoryPath.DirectoryOf(entry.Path);
            Collect(byDirectory, directory, entry);

            // Longest matching project directory. Two projects sharing one directory both own it: never fewer inputs.
            foreach (var candidate in RepositoryPath.SelfAndAncestors(directory).Reverse().Where(projectDirectories.ContainsKey))
            {
                foreach (var owner in projectDirectories[candidate])
                    Collect(owned, owner.Path, entry);
                break;
            }
        }

        central = new Lazy<CentralPackages>(() => CentralPackages.Parse(ReadFile(CentralPackages.RelativePath)));
    }

    public string Commit { get; }

    public DependencyMapDataset Map { get; }

    /// <summary>True when <see cref="Map"/> was derived from the tree rather than read from a committed copy.</summary>
    public bool MapDerived { get; }

    /// <summary>The dataset the maps generator would write for this commit's tree; fails as the generator would.</summary>
    private string Derive()
    {
        try
        {
            return DependencyMapJson.Serialize(new CommitTree(objects, Commit, tree));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Xml.XmlException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException($"Cannot derive the dependency map at {Commit}: {exception.Message}", exception);
        }
    }

    /// <summary>The content of a file in this commit, or null when the commit has no such file.</summary>
    public byte[]? ReadFile(string path) => tree.TryGetValue(path, out var entry) ? objects.Read(entry.ObjectId) : null;

    /// <summary>The backend projects in this commit: the projects the dependency map must describe.</summary>
    /// <remarks>
    /// Published history before the product split used repository-level <c>src/</c> and <c>tests/</c> roots. Newer
    /// commits use <c>Essentials/{src,apps,tests}</c> and <c>Extensions/&lt;capability&gt;/{src,tests}</c>. Choosing
    /// by the commit's own tree lets a record compare across the cutover without reinterpreting historical paths.
    /// A mixed tree fails closed: leaving a project at an old root after the cutover would otherwise escape the new
    /// map's inventory and silently contribute no package inputs.
    /// </remarks>
    public IEnumerable<string> ProjectFiles()
    {
        var consolidatedLayout = tree.Keys.Any(path => path.StartsWith("Essentials/", StringComparison.Ordinal) ||
                                                       path.StartsWith("Extensions/", StringComparison.Ordinal));
        if (consolidatedLayout && tree.Keys.Any(IsLegacyProjectPath))
            throw new InvalidOperationException(
                $"Commit {Commit} has projects under legacy src/ or tests/ roots beside Essentials/ or Extensions/. Every backend project must use the consolidated layout.");

        return tree.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal) &&
            (consolidatedLayout ? IsConsolidatedProjectPath(path) : IsLegacyProjectPath(path)));
    }

    private static bool IsLegacyProjectPath(string path) =>
        path.EndsWith(".csproj", StringComparison.Ordinal) &&
        (path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal));

    private static bool IsConsolidatedProjectPath(string path)
    {
        if (path.StartsWith("Essentials/src/", StringComparison.Ordinal) ||
            path.StartsWith("Essentials/apps/", StringComparison.Ordinal) ||
            path.StartsWith("Essentials/tests/", StringComparison.Ordinal))
            return true;

        var segments = path.Split('/');
        return segments.Length >= 4 && segments[0] == "Extensions" && segments[2] is "src" or "tests";
    }

    /// <summary>The MSBuild facts of a file in this commit, or null when the commit has no such file.</summary>
    public MsBuildFile? BuildFile(string path) => tree.TryGetValue(path, out var entry) ? objects.Parse(entry.ObjectId) : null;

    /// <summary>True when the project packs as a .NET tool, which carries every project it references inside itself.</summary>
    public bool PacksAsTool(MapNode node) => BuildFile(node.Path)?.PacksAsTool == true;

    /// <summary>
    /// The project and every project it references, directly or not: what a tool package carries. Fails when a
    /// reference has no node, since then nothing says which files it owns.
    /// </summary>
    public IReadOnlyList<MapNode> ReferenceClosure(MapNode node)
    {
        var closure = new List<MapNode>();
        var pending = new Stack<MapNode>([node]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current.Path))
                continue;
            closure.Add(current);
            foreach (var edge in current.Edges.Where(edge => edge.Internal))
                pending.Push(Map.ByPath.GetValueOrDefault(edge.Path!) ?? throw new InvalidOperationException(
                    $"{current.Path} references {edge.Path}, which the dependency map at {Commit} has no node for, so the files " +
                    $"{node.PackageId ?? node.Path} carries from it cannot be resolved."));
        }

        return closure.OrderBy(project => project.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The package-affecting inputs of a packable project at this commit.</summary>
    public PackageInputs InputsOf(MapNode package)
    {
        if (inputs.TryGetValue(package.Path, out var cached))
            return cached;

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var projects = PacksAsTool(package) ? ReferenceClosure(package) : [package];
        foreach (var project in projects)
        {
            var buildFiles = BuildFilesOf(project);
            foreach (var (path, value) in buildFiles)
                entries.TryAdd(PackageInputs.FilePrefix + path, value);

            var readers = buildFiles.Keys.Where(RepositoryPath.IsMsBuildFile).Select(path => (Path: path, File: BuildFile(path))).Where(file => file.File is not null).ToArray();
            var includedFiles = (owned.GetValueOrDefault(project.Path) ?? []).Where(file =>
                !IsDocumentation(file.Path) || readers.Any(reader => reader.File!.Names(file.Path, project.Directory, reader.Path)));
            foreach (var file in includedFiles)
                entries[PackageInputs.FilePrefix + file.Path] = $"{file.Mode} {file.ObjectId}";

            foreach (var edge in project.Edges.Where(edge => !edge.Internal))
            {
                var key = (edge.Kind == MapEdgeKind.PinnedTransitive ? PackageInputs.PinnedPrefix : PackageInputs.PackagePrefix) + edge.Id;
                var value = $"{edge.Version} {central.Value.EntriesFor(edge.Id)}";
                entries[key] = entries.TryGetValue(key, out var other) && other != value
                    ? string.Join(" | ", new[] { other, value }.Order(StringComparer.Ordinal))
                    : value;
            }
        }

        entries[PackageInputs.CentralPackagesKey] = central.Value.Remainder;
        return inputs[package.Path] = new PackageInputs(entries);
    }

    /// <summary>
    /// The build files a project's build reads, by path, valued for its inputs: its own MSBuild files, the ambient
    /// files above it, and everything any of those imports. An import that resolves to no file is recorded as absent,
    /// so the file appearing later is a change.
    /// </summary>
    private Dictionary<string, string> BuildFilesOf(MapNode project)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>();

        foreach (var file in (owned.GetValueOrDefault(project.Path) ?? []).Where(file => RepositoryPath.IsMsBuildFile(file.Path)))
            pending.Enqueue(file.Path);

        foreach (var directory in RepositoryPath.SelfAndAncestors(project.Directory).Where(directory => directory != project.Directory))
        {
            var ambientEntries = (byDirectory.GetValueOrDefault(directory) ?? []).Where(entry =>
                AmbientNames.Contains(entry.Path[directory.Length..].ToLowerInvariant()) && entry.Path != CentralPackages.RelativePath);
            foreach (var entry in ambientEntries)
                pending.Enqueue(entry.Path);
        }

        while (pending.TryDequeue(out var path))
        {
            if (files.ContainsKey(path))
                continue;

            if (!tree.TryGetValue(path, out var entry))
            {
                files[path] = "absent";
                continue;
            }

            var build = RepositoryPath.IsMsBuildFile(path) || path.EndsWith(".config", StringComparison.OrdinalIgnoreCase) ? BuildFile(path) : null;
            files[path] = owned.GetValueOrDefault(project.Path)?.Contains(entry) == true || build is null
                ? $"{entry.Mode} {entry.ObjectId}"
                : build.ContentDigest;

            // The usual chaining of a nested Directory.Build.props to the one above it: already an input.
            var imports = (RepositoryPath.IsMsBuildFile(path) ? build!.Imports : []).Where(import => !ImportsAmbientFileAbove(import));
            foreach (var import in imports)
            {
                var resolved = RepositoryPath.Expand(import, RepositoryPath.DirectoryOf(path), RepositoryPath.DirectoryOf(path), project.Directory);
                if (resolved is null || resolved.Contains('*', StringComparison.Ordinal) || resolved.Contains('?', StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"{path} at {Commit} imports '{import}', which the calculator cannot resolve to a file in the repository, so it " +
                        $"cannot tell when what that import brings into {project.PackageId ?? project.Path}'s build changes.");
                pending.Enqueue(resolved);
            }
        }

        return files;
    }

    /// <summary>
    /// <c>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', ...))</c> and the like: an import of the nearest
    /// ambient file above, every one of which is already among the inputs.
    /// </summary>
    private static bool ImportsAmbientFileAbove(string import)
    {
        const string function = "GetPathOfFileAbove(";
        var start = import.IndexOf(function, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        var name = import[(start + function.Length)..].TrimStart().TrimStart('\'', '"');
        var end = name.IndexOfAny(['\'', '"', ',', ')']);
        return end > 0 && AmbientNames.Contains(name[..end].Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Markdown: the one kind of owned file that can be left out (FR-002a), and only while no build file names it.
    /// Other kinds are consumed by build conventions no static reading can see — the package-manifest generator
    /// reads a project's <c>elsa-package.overrides.json</c> by name alone — so they always count.
    /// </summary>
    private static bool IsDocumentation(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private static void Collect(Dictionary<string, List<TreeEntry>> index, string key, TreeEntry entry)
    {
        if (!index.TryGetValue(key, out var list))
            index[key] = list = [];
        list.Add(entry);
    }
}

/// <summary>
/// Blobs and their parsed MSBuild facts, shared across commits: an unchanged file is read once, through one
/// <c>git cat-file --batch</c> that disposing closes. A read after that opens another.
/// </summary>
internal sealed class ObjectCache(GitRepository git) : IDisposable
{
    private readonly Dictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MsBuildFile> parsed = new(StringComparer.Ordinal);
    private BlobBatch? batch;

    public GitRepository Git { get; } = git;

    public byte[] Read(string objectId) =>
        blobs.TryGetValue(objectId, out var content) ? content : blobs[objectId] = (batch ??= Git.OpenBlobBatch()).Read(objectId);

    public void Dispose()
    {
        batch?.Dispose();
        batch = null;
    }

    public MsBuildFile Parse(string objectId) =>
        parsed.TryGetValue(objectId, out var file) ? file : parsed[objectId] = MsBuildFile.Parse(Read(objectId));
}
