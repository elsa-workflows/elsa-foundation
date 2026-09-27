using System.Text;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// A package's package-affecting inputs at one commit (spec 150 FR-002): one entry per input, keyed by what the input
/// is and valued by its content.
/// </summary>
/// <remarks>
/// <para>
/// Change detection and the input fingerprint are both functions of this one set, which is what keeps them from
/// disagreeing: a package is changed exactly when its set differs from the set at its last-published commit, and
/// its fingerprint (FR-018) is a digest of the same set. A file's key is its repository path and its value is
/// its git object, so a move changes the keys and a content edit the values; either is a change (FR-002).
/// </para>
/// <para>
/// Keys: <c>file:&lt;path&gt;</c> for an owned file or a build file the package's build reads;
/// <c>package:&lt;id&gt;</c> for an external dependency and its <c>Directory.Packages.props</c> entries;
/// <c>central-packages</c> for the rest of <c>Directory.Packages.props</c>.
/// </para>
/// </remarks>
public sealed class PackageInputs
{
    /// <summary>Leads the fingerprint's preimage, so a change to what counts as an input can never collide with the old form.</summary>
    public const string Scheme = "elsa-package-inputs/1";

    internal const string FilePrefix = "file:";
    internal const string PackagePrefix = "package:";
    internal const string CentralPackagesKey = "central-packages";

    internal PackageInputs(SortedDictionary<string, string> entries)
    {
        Entries = entries;
        var preimage = new StringBuilder(Scheme).Append('\n');
        foreach (var (key, value) in entries)
            preimage.Append(key).Append('\t').Append(value).Append('\n');

        Fingerprint = "sha256:" + MsBuildFile.Sha256(Encoding.UTF8.GetBytes(preimage.ToString()));
    }

    /// <summary>Every input, ordered by key.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary><c>sha256:&lt;hex&gt;</c> over <see cref="Scheme"/> and every entry: the input fingerprint (FR-018).</summary>
    public string Fingerprint { get; }

    /// <summary>Every input that differs between two sets, as a reader would name it, ordered by key.</summary>
    public static IReadOnlyList<string> Differences(PackageInputs before, PackageInputs after) =>
        before.Entries.Keys.Union(after.Entries.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => (key, was: before.Entries.GetValueOrDefault(key), @is: after.Entries.GetValueOrDefault(key)))
            .Where(input => input.was != input.@is)
            .Select(input => $"{Describe(input.key)} ({(input.was is null ? "added" : input.@is is null ? "removed" : "changed")})")
            .ToArray();

    private static string Describe(string key) =>
        key.StartsWith(FilePrefix, StringComparison.Ordinal) ? key[FilePrefix.Length..]
        : key.StartsWith(PackagePrefix, StringComparison.Ordinal) ? $"{CentralPackages.RelativePath}: {key[PackagePrefix.Length..]}"
        : $"{CentralPackages.RelativePath}, outside any directly referenced package's entry";
}
