using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// The canonical dependency map (spec 149) as it stood at one commit: where every project is, which ones pack,
/// under which package id and on which version line, and what each depends on.
/// </summary>
/// <remarks>
/// This is the calculator's only source of those facts; it never scans a project file for them (spec 149 SC-005).
/// The shape is checked when it is read, as spec 149 leaves to its consumers: a schema version this code was not
/// written for is refused rather than half-understood.
/// </remarks>
internal sealed class DependencyMapDataset
{
    public const string RelativePath = "docs/maps/dependency-map.json";

    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private DependencyMapDataset(IReadOnlyList<MapNode> nodes)
    {
        Nodes = nodes;
        Packable = nodes.Where(node => node.Packable).ToDictionary(node => node.PackageId!, StringComparer.OrdinalIgnoreCase);
        ByPath = nodes.ToDictionary(node => node.Path, StringComparer.Ordinal);
    }

    /// <summary>Every project node, packable or not, in the dataset's order.</summary>
    public IReadOnlyList<MapNode> Nodes { get; }

    /// <summary>Packable nodes by package id. Package ids compare case-insensitively, as a feed compares them.</summary>
    public IReadOnlyDictionary<string, MapNode> Packable { get; }

    /// <summary>Every node by its project path.</summary>
    public IReadOnlyDictionary<string, MapNode> ByPath { get; }

    public static DependencyMapDataset Parse(string json, string source)
    {
        Dataset dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<Dataset>(json, JsonOptions) ?? throw new InvalidOperationException($"{source} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{source} is not a readable dependency map: {exception.Message}", exception);
        }

        if (dataset.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"{source} has schema version {dataset.SchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)"}; " +
                $"this calculator reads version {SupportedSchemaVersion} and refuses any other (spec 149).");

        var nodes = (dataset.Nodes ?? throw new InvalidOperationException($"{source} lists no nodes.")).Select(node => node.Validate(source)).ToArray();

        var duplicates = nodes.Where(node => node.Packable).GroupBy(node => node.PackageId!, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"{source} gives more than one packable project the package id {string.Join(", ", duplicates)}.");

        var duplicatePaths = nodes.GroupBy(node => node.Path, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicatePaths.Length > 0)
            throw new InvalidOperationException($"{source} lists the project {string.Join(", ", duplicatePaths)} more than once.");

        return new DependencyMapDataset(nodes);
    }

    private sealed record Dataset(int? SchemaVersion, IReadOnlyList<NodeDto>? Nodes);

    private sealed record NodeDto(string? Path, string? Name, bool? Packable, string? PackageId, string? Line, IReadOnlyList<EdgeDto>? Edges)
    {
        public MapNode Validate(string source)
        {
            if (string.IsNullOrEmpty(Path) || !Path.EndsWith(".csproj", StringComparison.Ordinal) || Path.StartsWith('/') || Path.Contains('\\', StringComparison.Ordinal))
                throw new InvalidOperationException($"{source} has a node whose path '{Path}' is not a repository-relative .csproj path.");

            if (Packable is not bool packable)
                throw new InvalidOperationException($"{source}: {Path} does not say whether it is packable.");

            if (packable && (string.IsNullOrWhiteSpace(PackageId) || Line is not ("A" or "B")))
                throw new InvalidOperationException($"{source}: packable {Path} needs a package id and a version line of A or B.");

            var edges = (Edges ?? []).Select(edge => edge.Validate(source, Path)).ToArray();
            return new MapNode(Path, Name ?? System.IO.Path.GetFileNameWithoutExtension(Path), packable, packable ? PackageId : null, packable ? Line : null, edges);
        }
    }

    private sealed record EdgeDto(string? Type, string? Id, string? Path, string? Version)
    {
        public MapEdge Validate(string source, string node) => Type switch
        {
            "internal" when !string.IsNullOrEmpty(Path) => new MapEdge(true, Id ?? string.Empty, Path, null),
            "external" when !string.IsNullOrEmpty(Id) => new MapEdge(false, Id, null, Version),
            _ => throw new InvalidOperationException($"{source}: {node} has an edge that is neither an internal edge with a path nor an external edge with an id.")
        };
    }
}

/// <summary>One project of the dependency map.</summary>
internal sealed record MapNode(string Path, string Name, bool Packable, string? PackageId, string? Line, IReadOnlyList<MapEdge> Edges)
{
    /// <summary>The project's directory with its trailing slash, or empty at the repository root; what it owns.</summary>
    [JsonIgnore]
    public string Directory { get; } = Path[..(Path.LastIndexOf('/') + 1)];
}

/// <summary>A dependency: on another project in this repository (internal), or on an external package.</summary>
internal sealed record MapEdge(bool Internal, string Id, string? Path, string? Version);
