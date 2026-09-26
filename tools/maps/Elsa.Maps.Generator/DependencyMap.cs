using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>
/// Writes <c>docs/maps/dependency-map.json</c>, the canonical dependency map (spec 149): every project under
/// <c>src/</c> and <c>tests/</c> as a node, keyed by path, with its typed dependency edges.
/// </summary>
/// <remarks>
/// <para>
/// This is the dataset the markdown maps describing the project graph are projections of, and the one
/// publishing reads: its consumers resolve file ownership from it by longest matching project directory
/// (FR-006) and never run this generator (SC-005). It is a function of the tree alone. It holds no publish
/// state — the last-published record lives on the <c>publish-state</c> branch and nothing here reads or
/// writes it (FR-011) — and no source-file inventory (FR-009), so it changes only when the project graph does.
/// </para>
/// <para>
/// <c>schema_version</c> changes when the shape does in a way a consumer could misread. Checking it is each
/// consumer's concern at read time; generation does not gate on it.
/// </para>
/// </remarks>
public static class DependencyMap
{
    /// <summary>The dataset's repo-relative path.</summary>
    public const string RelativePath = "docs/maps/dependency-map.json";

    /// <summary>The shape this generator writes.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        // A data file, never embedded in HTML: keep '+' and '<' in ids and version ranges readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record Dataset(int SchemaVersion, string Generator, IReadOnlyList<ProjectFacts> Nodes);

    /// <summary>Writes the dataset and returns the repo-relative path written.</summary>
    public static IReadOnlyList<string> Generate(RepoContext repo, IReadOnlyList<ProjectFacts> projects)
    {
        var json = JsonSerializer.Serialize(new Dataset(SchemaVersion, "tools/maps/Elsa.Maps.Generator", projects), Options);
        MarkdownTable.Write(repo.Absolute(RelativePath), new StringBuilder(json).Append('\n'));
        return [RelativePath];
    }
}
