using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Versioning.Calculator;

/// <summary>One packable project's computed version.</summary>
/// <param name="PackageId">The package id, from the dependency map.</param>
/// <param name="Path">The project file, from the dependency map.</param>
/// <param name="Line"><c>A</c> or <c>B</c>.</param>
/// <param name="Version"><c>major.minor.patch</c>; packing adds the prerelease label (spec 150 FR-008).</param>
/// <param name="Affected">True when the package is to be packed and pushed; false when it keeps its last published version.</param>
/// <param name="LastPublished">Its entry in the last-published record, or null before its first publish.</param>
/// <param name="Fingerprint">The input fingerprint to stamp into the package (FR-018).</param>
/// <param name="Reasons">Why it is affected: the inputs that differ, or the rule that moved it; empty when it is not.</param>
public sealed record ComputedPackage(
    string PackageId,
    string Path,
    string Line,
    PackageVersionNumber Version,
    bool Affected,
    PublishedPackage? LastPublished,
    string Fingerprint,
    IReadOnlyList<string> Reasons);

/// <summary>
/// The calculator's result for one commit and one revision of the last-published record: the output #2080 packs from
/// and #2082 publishes from. <see cref="ToJson"/> is its one serialization, byte-identical for the same inputs.
/// </summary>
public sealed record VersionComputation(string Commit, string? RecordLastPublishCommit, VersionLineSettings Lines, IReadOnlyList<ComputedPackage> Packages)
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>The package ids to pack and push, ordered by package id.</summary>
    public IReadOnlyList<string> Affected => Packages.Where(package => package.Affected).Select(package => package.PackageId).ToArray();

    public ComputedPackage this[string packageId] =>
        Packages.SingleOrDefault(package => string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"No packable project has package id {packageId}.");

    /// <summary>Snake-case JSON, two-space indentation, LF line endings and a trailing newline.</summary>
    public string ToJson() => new StringBuilder(JsonSerializer.Serialize(
        new Output(
            SchemaVersion,
            Commit,
            RecordLastPublishCommit,
            new SortedDictionary<string, string>(StringComparer.Ordinal) { ["A"] = Lines.LineA.ToString(), ["B"] = Lines.LineB.ToString() },
            Affected,
            Packages.Select(package => new PackageOutput(
                package.PackageId,
                package.Path,
                package.Line,
                package.Version.Numeric,
                package.Affected,
                package.LastPublished is { } published ? new PublishedOutput(published.Version.ToString(), published.Commit) : null,
                package.Fingerprint,
                package.Reasons)).ToArray()),
        JsonOptions)).Append('\n').ToString();

    private sealed record Output(
        int SchemaVersion,
        string Commit,
        string? LastPublishCommit,
        IReadOnlyDictionary<string, string> Lines,
        IReadOnlyList<string> Affected,
        IReadOnlyList<PackageOutput> Packages);

    private sealed record PackageOutput(
        string PackageId,
        string Path,
        string Line,
        string Version,
        bool Affected,
        PublishedOutput? LastPublished,
        string Fingerprint,
        IReadOnlyList<string> Reasons);

    private sealed record PublishedOutput(string Version, string Commit);
}
