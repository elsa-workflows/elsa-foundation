using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Maps.Projects;

/// <summary>
/// The canonical dependency-map dataset (spec 149) as JSON: what the maps generator writes to
/// <c>docs/maps/dependency-map.json</c> and what the versioning calculator derives for a commit that has no committed copy
/// (spec 259).
/// </summary>
/// <remarks>
/// <para>
/// One serializer for both, so a derived dataset and a generated one for the same tree are the same bytes, and the
/// calculator reads either through the one validator it already has. The <c>generator</c> field names the maps generator
/// in both cases for the same reason.
/// </para>
/// <para>
/// <c>schema_version</c> changes when the shape does in a way a consumer could misread: version 2 added the
/// pinned-transitive edges. Checking it is each consumer's concern at read time; serialization does not gate on it.
/// </para>
/// </remarks>
public static class DependencyMapJson
{
    /// <summary>The dataset's repo-relative path, where the generator writes it and where older commits carry it.</summary>
    public const string RelativePath = "docs/maps/dependency-map.json";

    /// <summary>The shape this serializer writes.</summary>
    public const int SchemaVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        // A data file, never embedded in HTML: keep '+' and '<' in ids and version ranges readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record Dataset(int SchemaVersion, string Generator, IReadOnlyList<ProjectFacts> Nodes);

    /// <summary>The dataset for <paramref name="tree"/>, scanned from it, ending in a newline.</summary>
    public static string Serialize(IRepositoryTree tree) => Serialize(tree, ProjectGraph.Read(tree));

    /// <summary>The dataset for <paramref name="tree"/>, from projects <see cref="ProjectGraph.Read"/> already returned for it.</summary>
    public static string Serialize(IRepositoryTree tree, IReadOnlyList<ProjectFacts> projects) =>
        JsonSerializer.Serialize(new Dataset(SchemaVersion, "tools/maps/Elsa.Maps.Generator", PinnedTransitiveDependencies.Attach(tree, projects)), Options) + "\n";
}
