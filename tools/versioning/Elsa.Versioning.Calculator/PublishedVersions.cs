using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Versioning.Calculator;

/// <summary>One package id's entry in the last-published record: the version last pushed, and the commit it was built from.</summary>
public sealed record PublishedPackage(string PackageId, PackageVersionNumber Version, string Commit);

/// <summary>
/// The last-published record, <c>published-versions.json</c> on the <c>publish-state</c> branch (spec 150 FR-014).
/// </summary>
/// <remarks>
/// <para>
/// The calculator only reads it. It is an input alongside the commit being built, never part of that commit's tree,
/// so nothing about where it is read from, or which revision of it is read, enters change detection (FR-016).
/// </para>
/// <para>
/// Reading is strict: an unknown schema version, an unknown property, a duplicate package id or a malformed version
/// or commit fails the read. A record that is wrong yields wrong versions, and no gate downstream can tell.
/// </para>
/// </remarks>
public sealed class PublishedVersions
{
    /// <summary>Where the file sits on the <c>publish-state</c> branch.</summary>
    public const string DefaultPath = "published-versions.json";

    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly Dictionary<string, PublishedPackage> byId;

    /// <param name="lastPublishCommit">
    /// The <c>main</c> commit the most recent publish was built from (FR-021). Null only before the first publish,
    /// when there are no entries.
    /// </param>
    /// <param name="packages">One entry per package id; entries are never removed (FR-014).</param>
    public PublishedVersions(string? lastPublishCommit, IEnumerable<PublishedPackage> packages)
    {
        Packages = packages.OrderBy(package => package.PackageId, StringComparer.Ordinal).ToArray();

        if (lastPublishCommit is null ? Packages.Count > 0 : !GitRepository.IsFullObjectId(lastPublishCommit))
            throw new InvalidOperationException(
                "The record's last_publish_commit must be a full lowercase commit id, and may be null only while the record has no entries.");

        byId = new Dictionary<string, PublishedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageId) || !GitRepository.IsFullObjectId(package.Commit))
                throw new InvalidOperationException($"The record entry '{package.PackageId}' needs a package id and a full lowercase commit id.");

            if (!byId.TryAdd(package.PackageId, package))
                throw new InvalidOperationException($"The record holds more than one entry for package id '{package.PackageId}' (package ids compare case-insensitively).");
        }

        LastPublishCommit = lastPublishCommit;
    }

    public string? LastPublishCommit { get; }

    /// <summary>Every entry, ordered by package id.</summary>
    public IReadOnlyList<PublishedPackage> Packages { get; }

    /// <summary>The entry for a package id, compared case-insensitively as a feed compares ids; null when there is none.</summary>
    public PublishedPackage? Find(string packageId) => byId.GetValueOrDefault(packageId);

    /// <summary>Reads the record from a file.</summary>
    public static PublishedVersions Load(string path) => Parse(File.ReadAllBytes(path), path);

    /// <summary>Reads the record from a revision, such as <c>origin/publish-state</c>, without checking it out.</summary>
    public static PublishedVersions Load(GitRepository repository, string revision, string path = DefaultPath) =>
        Parse(repository.ReadBlob($"{revision}:{path}"), $"{revision}:{path}");

    public static PublishedVersions Parse(byte[] utf8Json, string source)
    {
        RecordDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<RecordDto>(utf8Json, JsonOptions) ?? throw new InvalidOperationException($"{source} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{source} is not a readable last-published record: {exception.Message}", exception);
        }

        if (dto.SchemaVersion != SchemaVersion)
            throw new InvalidOperationException($"{source} has schema version {dto.SchemaVersion}; this calculator reads version {SchemaVersion} and refuses any other.");

        try
        {
            return new PublishedVersions(dto.LastPublishCommit, (dto.Packages ?? throw new InvalidOperationException("It has no packages list.")).Select(entry =>
                new PublishedPackage(
                    entry.PackageId ?? string.Empty,
                    PackageVersionNumber.Parse(entry.Version ?? string.Empty, $"package '{entry.PackageId}'"),
                    entry.Commit ?? string.Empty)));
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{source}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The record's one serialization: entries sorted by package id, two-space indentation, LF line endings, a
    /// trailing newline and no timestamps, so a revision's diff shows only what a publish changed (FR-014).
    /// </summary>
    public string Serialize()
    {
        var dto = new RecordDto(
            SchemaVersion,
            LastPublishCommit,
            Packages.Select(package => new EntryDto(package.PackageId, package.Version.ToString(), package.Commit)).ToArray());
        return new StringBuilder(JsonSerializer.Serialize(dto, JsonOptions)).Append('\n').ToString();
    }

    private sealed record RecordDto(int SchemaVersion, string? LastPublishCommit, IReadOnlyList<EntryDto>? Packages);

    private sealed record EntryDto(string? PackageId, string? Version, string? Commit);
}
