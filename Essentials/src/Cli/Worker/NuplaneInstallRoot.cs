namespace Elsa.Cli.Worker;

/// <summary>One installed package directory found under a package root, before any loader is involved.</summary>
public sealed record InstalledPackage(string Id, string Version, string InstallPath, string FeedName, DateTimeOffset InstalledAtUtc);

/// <summary>
/// The completion-marker probe for a <c>--packages &lt;dir&gt;</c> root that carries no state file (FR-006).
/// Nuplane's install layout is <c>&lt;root&gt;/&lt;feed&gt;/&lt;packageId&gt;/&lt;version&gt;/</c>, a raw
/// extracted package marked complete by a <c>.nuplane-ready</c> file, staged through a <c>.tmp</c> directory
/// that is not part of the installed set.
/// </summary>
/// <remarks>
/// This is a directory scan, not a second implementation of Nuplane's loader or of its state-file format
/// (ADR 0076 D12): what it produces is handed to Nuplane's own entry point to load. The two rules it keeps
/// are the ones that decide whether a package exists at all — an extraction that never completed carries no
/// marker and is treated as not installed rather than as a corrupt one, and a staging directory is skipped
/// rather than read as a feed.
/// </remarks>
public static class NuplaneInstallRoot
{
    /// <summary>The file Nuplane writes into an install directory once extraction completed.</summary>
    public const string ReadyMarker = ".nuplane-ready";

    /// <summary>The staging directory an in-progress extraction uses; never part of the installed set.</summary>
    public const string StagingDirectory = ".tmp";

    /// <summary>The state file a reconciled host writes beside its install root.</summary>
    public const string StateFileName = "store-state.json";

    /// <summary>The install root a host uses when its configuration names none, relative to the host directory.</summary>
    public static string DefaultStateFile(string hostDirectory) => Path.Join(hostDirectory, ".nuplane", StateFileName);

    /// <summary>
    /// Every completed install under <paramref name="root"/>. Refuses rather than guesses when one package
    /// id is installed at two versions with nothing to choose between them: no state file means no
    /// activation record, and picking the newest would script against a version the host may never load.
    /// </summary>
    public static IReadOnlyList<InstalledPackage> Probe(string root)
    {
        if (!Directory.Exists(root))
            throw WorkerRefusal.Resolution("packages-root-missing", $"The package root '{root}' does not exist.");

        var installed = new List<InstalledPackage>();
        foreach (var feed in Directories(root))
            foreach (var package in Directories(feed))
                foreach (var version in Directories(package))
                {
                    var marker = Path.Join(version, ReadyMarker);
                    if (!File.Exists(marker))
                        continue;
                    installed.Add(new(
                        Path.GetFileName(package),
                        Path.GetFileName(version),
                        version,
                        Path.GetFileName(feed),
                        File.GetLastWriteTimeUtc(marker)));
                }

        var ambiguous = installed
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' is installed at {string.Join(", ", group.Select(package => package.Version).Order(StringComparer.Ordinal))}.")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (ambiguous.Length > 0)
        {
            throw WorkerRefusal.Resolution(
                "packages-ambiguous",
                $"'{root}' has no {StateFileName}, so nothing there records which version is active, and more than one is installed.",
                ambiguous);
        }

        return installed;
    }

    /// <summary>Immediate subdirectories, with the staging directory skipped at every level.</summary>
    private static IEnumerable<string> Directories(string parent) =>
        Directory.EnumerateDirectories(parent)
            .Where(directory => !string.Equals(Path.GetFileName(directory), StagingDirectory, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
