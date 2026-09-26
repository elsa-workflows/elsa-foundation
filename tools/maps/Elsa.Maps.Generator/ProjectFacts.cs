using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Elsa.Maps.Generator;

/// <summary>
/// One project in the repository: a node of the canonical dependency map (spec 149), with the grouping
/// labels the maps report and its typed dependency edges.
/// </summary>
/// <param name="RelativePath">Repo-relative, forward-slashed path to the <c>.csproj</c>; the node's identity.</param>
/// <param name="Name">Assembly/project name, i.e. the file name without <c>.csproj</c>.</param>
/// <param name="Kind"><c>source</c> or <c>test</c>.</param>
/// <param name="Domain">Top-level grouping, e.g. <c>Elsa.Workflows</c>.</param>
/// <param name="SubDomain">Remainder after the domain prefix, or <c>-</c> / <c>(root)</c>.</param>
/// <param name="Role">Heuristic navigation label — not a constitution verdict.</param>
/// <param name="DeclaredTargetFramework">The <c>TargetFramework</c> the project file itself sets; null when inherited.</param>
/// <param name="Packable">Whether packing the project produces a package, with MSBuild's defaults applied.</param>
/// <param name="PackageId">The package identity; null unless <paramref name="Packable"/>.</param>
/// <param name="Line">Version line, <c>A</c> or <c>B</c> (ADR 0067); null unless <paramref name="Packable"/>.</param>
/// <param name="Edges">Direct dependencies: internal edges by target path, then external edges by id and version.</param>
public sealed record ProjectFacts(
    [property: JsonPropertyName("path")] string RelativePath,
    string Name,
    string Kind,
    string Domain,
    string SubDomain,
    string Role,
    string? DeclaredTargetFramework,
    bool Packable,
    string? PackageId,
    string? Line,
    IReadOnlyList<DependencyEdge> Edges)
{
    /// <summary>Directly referenced project names, ordinally sorted and deduplicated.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> References =>
        Edges.OfType<InternalEdge>().Select(edge => edge.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Directly referenced external packages, ordered by id and version.</summary>
    [JsonIgnore]
    public IEnumerable<ExternalEdge> Packages => Edges.OfType<ExternalEdge>();
}

/// <summary>A direct dependency on a package identity, typed by whether the target is a project in this repository.</summary>
/// <param name="Id">The target's package identity.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(InternalEdge), "internal")]
[JsonDerivedType(typeof(ExternalEdge), "external")]
public abstract record DependencyEdge([property: JsonPropertyOrder(-1)] string Id);

/// <summary>A <c>ProjectReference</c>, resolved against the referencing project's directory as MSBuild resolves it.</summary>
/// <param name="RelativePath">Repo-relative path to the target <c>.csproj</c>.</param>
public sealed record InternalEdge(string Id, [property: JsonPropertyName("path")] string RelativePath) : DependencyEdge(Id)
{
    /// <summary>The target's project name, i.e. its file name without <c>.csproj</c>.</summary>
    [JsonIgnore]
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
}

/// <summary>A <c>PackageReference</c>, with the version central package management resolves for it.</summary>
/// <param name="Version">The declared version; null when neither the reference nor <c>Directory.Packages.props</c> supplies one.</param>
public sealed record ExternalEdge(string Id, string? Version) : DependencyEdge(Id)
{
    /// <summary>The version as the markdown maps print it.</summary>
    [JsonIgnore]
    public string DisplayVersion => Version ?? "(unspecified)";
}

/// <summary>Reads the project graph out of the repository's <c>.csproj</c> files.</summary>
/// <remarks>
/// This is the one scan of the project files (spec 149, FR-001). Every map that describes the project graph
/// is a projection of what it returns, and no map generator reads a <c>.csproj</c> itself. The solution-filter
/// and EF-suite selectors are not maps: they validate <c>Elsa.Server.slnx</c> and read its projects directly.
/// </remarks>
public static class ProjectGraph
{
    /// <summary>SDKs whose props default <c>IsPackable</c> to false, as the Web and Worker SDKs do.</summary>
    private static readonly string[] NonPackableSdks = ["Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker"];

    /// <summary>Reads every source and test project, in ordinal path order.</summary>
    public static IReadOnlyList<ProjectFacts> Read(RepoContext repo)
    {
        var packages = PackageVersions.Load(repo);
        var lineAMembers = ReadLineAMembers(repo);
        var inheritedProperties = new Dictionary<string, XDocument?>(StringComparer.Ordinal);

        var files = repo.ListFiles(RepoLayout.ProjectPathspecs).Select(path => ProjectFile.Load(repo, path)).ToArray();

        // Case-insensitive so an Include whose casing differs from the tracked path still resolves to the node, and
        // the edge records the node's own path rather than the Include's spelling of it.
        var byPath = files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        return files.Select(file =>
        {
            var kind = RepoLayout.Kind(file.RelativePath);
            var domain = DomainGroup(file.Name);
            var packable = IsPackable(repo, file, inheritedProperties);

            return new ProjectFacts(
                file.RelativePath,
                file.Name,
                kind,
                domain,
                SubDomain(file.Name, domain),
                Role(file.Name, kind),
                file.Property("TargetFramework"),
                packable,
                packable ? file.PackageIdentity : null,
                packable ? LineOf(file.Name, lineAMembers) : null,
                [.. InternalEdges(repo, file, byPath), .. ExternalEdges(file, packages)]);
        }).ToArray();
    }

    /// <summary>
    /// The project whose directory is the longest prefix of <paramref name="relativePath"/>, or null
    /// when the file sits outside every project.
    /// </summary>
    public static TProject? OwningProject<TProject>(
        IReadOnlyList<TProject> projects,
        string relativePath,
        Func<TProject, string> projectPath)
        where TProject : class =>
        projects
            .Select(project => (project, directory: Path.GetDirectoryName(projectPath(project))?.Replace('\\', '/') ?? string.Empty))
            .Where(candidate => relativePath.StartsWith(candidate.directory + "/", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.directory.Length)
            .Select(candidate => candidate.project)
            .FirstOrDefault();

    /// <summary>The top-level grouping a project belongs to.</summary>
    public static string DomainGroup(string project)
    {
        if (project is "Elsa.Workbench" or "Workbench") return "Elsa.Workbench";
        if (project.StartsWith("Test.", StringComparison.Ordinal)) return "Test";
        if (project.StartsWith("Elsa3.", StringComparison.Ordinal)) return "Elsa3";

        if (!project.StartsWith("Elsa.", StringComparison.Ordinal)) return "Other";

        // "Elsa.Workflows.Runtime.Core" groups under "Elsa.Workflows"; a bare "Elsa" keeps its single segment.
        var segments = project.Split('.');
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
    }

    /// <summary>
    /// Every <c>ProjectReference</c>, resolved against the referencing project's directory, deduplicated by target.
    /// </summary>
    /// <remarks>
    /// A reference that cannot be resolved fails generation rather than being recorded under a guessed identity:
    /// publishing reads these edges, so a dangling one would be wrong versions rather than a stale document.
    /// A target outside <c>src/</c> and <c>tests/</c> (a tool or a sample) is still internal, but has no node.
    /// </remarks>
    private static IEnumerable<InternalEdge> InternalEdges(RepoContext repo, ProjectFile file, IReadOnlyDictionary<string, ProjectFile> byPath)
    {
        var directory = Path.GetDirectoryName(repo.Absolute(file.RelativePath))!;

        return file.Items("ProjectReference")
            .Select(item => item.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include =>
            {
                if (include.Contains("$(", StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file.RelativePath}: ProjectReference '{include}' uses an MSBuild property, which the maps generator cannot resolve.");

                var target = Path.GetRelativePath(repo.Root, Path.GetFullPath(include.Replace('\\', Path.DirectorySeparatorChar), directory)).Replace('\\', '/');
                if (target.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(target))
                    throw new InvalidOperationException($"{file.RelativePath}: ProjectReference '{include}' resolves outside the repository.");

                if (byPath.TryGetValue(target, out var node))
                    return new InternalEdge(node.PackageIdentity, node.RelativePath);

                if (!File.Exists(repo.Absolute(target)))
                    throw new InvalidOperationException($"{file.RelativePath}: ProjectReference '{include}' names a project that does not exist ({target}).");

                return new InternalEdge(ProjectFile.Load(repo, target).PackageIdentity, target);
            })
            .DistinctBy(edge => edge.RelativePath, StringComparer.Ordinal)
            .OrderBy(edge => edge.RelativePath, StringComparer.Ordinal);
    }

    /// <summary>Every <c>PackageReference</c> with its resolved version, deduplicated and ordered by id then version.</summary>
    private static IEnumerable<ExternalEdge> ExternalEdges(ProjectFile file, PackageVersions packages) =>
        file.Items("PackageReference")
            .Select(item => (Id: item.Attribute("Include")?.Value ?? string.Empty,
                Inline: item.Attribute("VersionOverride")?.Value is { Length: > 0 } versionOverride ? versionOverride : item.Attribute("Version")?.Value ?? string.Empty))
            .Where(reference => reference.Id.Length > 0)
            .Select(reference => new ExternalEdge(reference.Id, packages.Resolve(reference.Id, reference.Inline, file.Name)))
            .Distinct()
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ThenBy(edge => edge.Version, StringComparer.Ordinal);

    /// <summary>
    /// Whether packing produces a package, applying MSBuild's precedence: the project file, then the nearest
    /// <c>Directory.Build.props</c> (imported before the project body), then the SDK defaults — false for the
    /// Web and Worker SDKs and for test projects, true otherwise.
    /// </summary>
    /// <remarks>
    /// <c>tests/Directory.Build.props</c> is why a test project that declares nothing is not packable, and
    /// <c>Microsoft.NET.Test.Sdk</c> sets <c>IsTestProject</c> for the projects that reference it. Pack
    /// compares the value to <c>true</c> case-insensitively, so anything else counts as not packable.
    /// </remarks>
    private static bool IsPackable(RepoContext repo, ProjectFile file, Dictionary<string, XDocument?> inheritedProperties)
    {
        var declared = file.Property("IsPackable") ?? InheritedProperty(repo, file.RelativePath, "IsPackable", inheritedProperties);
        if (declared is not null)
            return string.Equals(declared, "true", StringComparison.OrdinalIgnoreCase);

        var isHost = (file.Document.Root!.Attribute("Sdk")?.Value ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries)
            .Any(sdk => NonPackableSdks.Contains(sdk.Split('/')[0], StringComparer.OrdinalIgnoreCase));
        var isTest = string.Equals(file.Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
                     || file.Items("PackageReference").Any(item => string.Equals(item.Attribute("Include")?.Value, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));

        return !isHost && !isTest;
    }

    /// <summary>
    /// A property from the nearest <c>Directory.Build.props</c> above the project, which is the only one MSBuild
    /// imports automatically.
    /// </summary>
    private static string? InheritedProperty(RepoContext repo, string relativePath, string name, Dictionary<string, XDocument?> cache)
    {
        var directory = relativePath;
        do
        {
            var separator = directory.LastIndexOf('/');
            directory = separator < 0 ? string.Empty : directory[..separator];
            var props = directory.Length == 0 ? "Directory.Build.props" : $"{directory}/Directory.Build.props";

            if (!cache.TryGetValue(props, out var document))
                cache[props] = document = File.Exists(repo.Absolute(props)) ? XDocument.Load(repo.Absolute(props)) : null;

            if (document is not null)
                return new ProjectFile(props, document).Property(name);
        } while (directory.Length > 0);

        return null;
    }

    /// <summary>
    /// The raw <c>ElsaVersionLineAMembers</c> text from <c>VersionLines.props</c>, the one reviewed Line A list
    /// (ADR 0067) that <c>Directory.Build.props</c> also reads.
    /// </summary>
    private static string ReadLineAMembers(RepoContext repo)
    {
        const string path = "VersionLines.props";
        if (!File.Exists(repo.Absolute(path)))
            throw new InvalidOperationException($"{path} is missing; the dependency map reads Line A membership from it.");

        return new ProjectFile(path, XDocument.Load(repo.Absolute(path))).Property("ElsaVersionLineAMembers")
               ?? throw new InvalidOperationException($"{path} declares no ElsaVersionLineAMembers.");
    }

    /// <summary>
    /// A project's version line, with the whole-name semantics <c>Directory.Build.props</c> gives it: MSBuild tests
    /// <c>$(ElsaVersionLineAMembers.Contains(';$(MSBuildProjectName);'))</c>, so <c>Elsa.Tasks</c> is Line B even
    /// though <c>Elsa.Tasks.Core</c> is Line A.
    /// </summary>
    private static string LineOf(string projectName, string lineAMembers) =>
        lineAMembers.Contains($";{projectName};", StringComparison.Ordinal) ? "A" : "B";

    private static string SubDomain(string project, string domain)
    {
        if (domain is "Elsa.Workbench" or "Test" or "Elsa3" or "Other") return "-";

        var tail = project.StartsWith(domain, StringComparison.Ordinal) ? project[domain.Length..] : project;
        tail = tail.StartsWith('.') ? tail[1..] : tail;
        return tail.Length > 0 ? tail : "(root)";
    }

    private static string Role(string project, string kind)
    {
        if (kind == "test") return "test";
        if (project is "Elsa.Workbench" or "Workbench") return "host";
        if (project.EndsWith(".Core", StringComparison.Ordinal)) return "contract";

        if (EndsWithAny(project, ".Strategies", ".Schedules", ".Libraries")) return "helper";
        if (EndsWithAny(project, ".Sqlite", ".EFCore", ".FileSystem", ".Jint", ".Liquid")) return "provider/implementation";

        return "feature/implementation";
    }

    private static bool EndsWithAny(string value, params string[] suffixes) =>
        suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>One parsed MSBuild file, read for its evaluation-time properties and items.</summary>
    private sealed record ProjectFile(string RelativePath, XDocument Document)
    {
        public static ProjectFile Load(RepoContext repo, string relativePath) => new(relativePath, XDocument.Load(repo.Absolute(relativePath)));

        public string Name => Path.GetFileNameWithoutExtension(RelativePath);

        /// <summary><c>PackageId</c> defaults to <c>AssemblyName</c>, which defaults to the project name.</summary>
        public string PackageIdentity => Property("PackageId") ?? Property("AssemblyName") ?? Name;

        /// <summary>
        /// The last value a top-level <c>PropertyGroup</c> assigns, since MSBuild lets the last assignment win; null
        /// when none assigns a non-empty one.
        /// </summary>
        /// <remarks>
        /// A conditional or computed assignment fails generation instead of being read as a literal: this is a
        /// text scan, not an evaluation, and the dataset feeds publishing, so a guessed package id or packable
        /// state must be loud rather than quietly wrong.
        /// </remarks>
        public string? Property(string name)
        {
            var element = Document.Root!.Elements().Where(group => group.Name.LocalName == "PropertyGroup")
                .Elements().LastOrDefault(property => property.Name.LocalName == name);
            if (element is null) return null;

            if (element.Attribute("Condition") is not null || element.Parent!.Attribute("Condition") is not null || element.Value.Contains("$(", StringComparison.Ordinal))
                throw new InvalidOperationException($"{RelativePath}: <{name}> is conditional or computed, which the maps generator cannot evaluate.");

            var value = element.Value.Trim();
            return value.Length > 0 ? value : null;
        }

        /// <summary>Every item of the given type in a top-level <c>ItemGroup</c>, in document order.</summary>
        public IEnumerable<XElement> Items(string type) =>
            Document.Root!.Elements().Where(group => group.Name.LocalName == "ItemGroup")
                .Elements().Where(item => item.Name.LocalName == type);
    }
}
