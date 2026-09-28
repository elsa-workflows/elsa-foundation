using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.Versioning.Publisher;

/// <summary>A <c>.nupkg</c> as the publisher reads it: its id, version, input fingerprint and the ids it depends on.</summary>
/// <param name="Path">The file.</param>
/// <param name="PackageId">The nuspec's id.</param>
/// <param name="Version">The nuspec's version.</param>
/// <param name="Fingerprint">The input fingerprint the package carries (spec 150 FR-018).</param>
/// <param name="DependencyIds">Every id any of its dependency groups names.</param>
public sealed record PackedPackage(string Path, string PackageId, string Version, string Fingerprint, IReadOnlyList<string> DependencyIds)
{
    /// <summary>Where a computed pack puts the fingerprint: at the package root (#2080).</summary>
    public const string FingerprintEntry = "elsa-input-fingerprint.json";

    public static PackedPackage Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var metadata = Metadata(archive, path);

        string Required(string name) =>
            metadata.Elements().SingleOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{path}'s nuspec has no {name}.");

        return new PackedPackage(
            path,
            Required("id"),
            Required("version"),
            ReadFingerprint(archive, path),
            metadata.Descendants()
                .Where(element => element.Name.LocalName == "dependency")
                .Select(element => (string?)element.Attribute("id"))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    /// <summary>The fingerprint a package carries, read from its bytes; the feed's copy is read the same way.</summary>
    /// <exception cref="InvalidOperationException">The package carries none, or not in the schema a computed pack writes.</exception>
    public static string ReadFingerprint(Stream package, string source)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        return ReadFingerprint(archive, source);
    }

    /// <summary>
    /// The nuspec's <c>repository</c> <c>commit</c> a package carries — the commit it was built from (spec 150 FR-018)
    /// — read from its bytes; the feed's copy is read the same way, for the repair workflow (FR-019).
    /// </summary>
    /// <exception cref="InvalidOperationException">The nuspec names no repository commit.</exception>
    public static string ReadSourceCommit(Stream package, string source)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var metadata = Metadata(archive, source);
        return metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "repository")?.Attribute("commit")?.Value is { Length: > 0 } commit
            ? commit
            : throw new InvalidOperationException($"{source}'s nuspec names no repository commit.");
    }

    private static XElement Metadata(ZipArchive archive, string source)
    {
        var nuspecs = archive.Entries.Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nuspecs.Length != 1)
            throw new InvalidOperationException($"{source} holds {nuspecs.Length} nuspec files at its root; a package holds exactly one.");

        using var stream = nuspecs[0].Open();
        return XDocument.Load(stream).Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata")
               ?? throw new InvalidOperationException($"{source}'s nuspec has no metadata.");
    }

    private static string ReadFingerprint(ZipArchive archive, string source)
    {
        var entry = archive.GetEntry(FingerprintEntry)
                    ?? throw new InvalidOperationException($"{source} carries no {FingerprintEntry}, so it was not packed from computed versions.");

        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("schema_version").GetInt32() == 1 &&
                   document.RootElement.GetProperty("fingerprint").GetString() is { Length: > 0 } fingerprint
                ? fingerprint
                : throw new InvalidOperationException($"{source}'s {FingerprintEntry} is not schema version 1 with a fingerprint.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"{source}'s {FingerprintEntry} is unreadable: {exception.Message}", exception);
        }
    }
}
