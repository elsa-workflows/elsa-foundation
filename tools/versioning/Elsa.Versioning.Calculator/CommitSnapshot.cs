using System.Text;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// One commit as change detection sees it: its tree, the dependency map committed in it, and from those, the
/// package-affecting inputs of any package at that commit.
/// </summary>
/// <remarks>
/// <para>
/// Everything comes from the commit's own objects, including the dependency map, so ownership is resolved "from the
/// dependency map at each of the two commits" (spec 150 FR-002) and a package that moved is found at its old path in
/// the old commit and its new path in the new one. Nothing is keyed on a path across commits except the inputs'
/// own keys, whose difference is exactly what a move is.
/// </para>
/// <para>A package's inputs are:</para>
/// <list type="bullet">
/// <item>every file its project owns — the longest matching project directory (spec 149 FR-006) — except a Markdown
/// document none of its build files names (FR-002a); for a project that packs as a tool, every file of every project it
/// references too, since a tool carries its references' builds inside its own package;</item>
/// <item>the build files every build of it reads: each <c>Directory.Build.props</c>, <c>Directory.Build.targets</c>,
/// <c>Directory.Build.rsp</c>, <c>NuGet.config</c>, <c>global.json</c> and non-root <c>Directory.Packages.props</c> in
/// its directory's ancestors, and every file those and its own MSBuild files import (FR-004);</item>
/// <item>for each external package it references directly, that package's <c>Directory.Packages.props</c> entries
/// (FR-003), and everything in that file other than <c>PackageVersion</c> entries as one input (FR-004,
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

    public CommitSnapshot(ObjectCache cache, string commit)
    {
        objects = cache;
        Commit = commit;
        tree = objects.Git.ListTree(commit).ToDictionary(entry => entry.Path, StringComparer.Ordinal);

        Map = tree.TryGetValue(DependencyMapDataset.RelativePath, out var mapEntry)
            ? DependencyMapDataset.Parse(Encoding.UTF8.GetString(objects.Read(mapEntry.ObjectId)), $"{DependencyMapDataset.RelativePath} at {commit}")
            : throw new InvalidOperationException(
                $"Commit {commit} has no {DependencyMapDataset.RelativePath}; the calculator resolves ownership from the dependency map committed with each commit it compares.");

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

    /// <summary>The content of a file in this commit, or null when the commit has no such file.</summary>
    public byte[]? ReadFile(string path) => tree.TryGetValue(path, out var entry) ? objects.Read(entry.ObjectId) : null;

    /// <summary>Every <c>.csproj</c> under <c>src/</c> or <c>tests/</c>: the projects the dependency map must describe.</summary>
    public IEnumerable<string> ProjectFiles() =>
        tree.Keys.Where(path => (path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal)) &&
                                path.EndsWith(".csproj", StringComparison.Ordinal));

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
                var key = PackageInputs.PackagePrefix + edge.Id;
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

        foreach (var file in owned.GetValueOrDefault(project.Path) ?? [])
        {
            if (RepositoryPath.IsMsBuildFile(file.Path))
                pending.Enqueue(file.Path);
        }

        foreach (var directory in RepositoryPath.SelfAndAncestors(project.Directory).Where(directory => directory != project.Directory))
        {
            foreach (var entry in byDirectory.GetValueOrDefault(directory) ?? [])
            {
                var name = entry.Path[directory.Length..].ToLowerInvariant();
                if (AmbientNames.Contains(name) && entry.Path != CentralPackages.RelativePath)
                    pending.Enqueue(entry.Path);
            }
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

/// <summary>Blobs and their parsed MSBuild facts, shared across commits: an unchanged file is read once.</summary>
internal sealed class ObjectCache(GitRepository git)
{
    private readonly Dictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MsBuildFile> parsed = new(StringComparer.Ordinal);

    public GitRepository Git { get; } = git;

    public byte[] Read(string objectId) =>
        blobs.TryGetValue(objectId, out var content) ? content : blobs[objectId] = Git.ReadBlob(objectId);

    public MsBuildFile Parse(string objectId) =>
        parsed.TryGetValue(objectId, out var file) ? file : parsed[objectId] = MsBuildFile.Parse(Read(objectId));
}
