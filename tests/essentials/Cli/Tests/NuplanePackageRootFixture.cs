using Acme.Widgets;
using Elsa.Cli.Worker;
using System.Text.Json;

namespace Elsa.Cli.Tests;

/// <summary>Writes the small installed-package shapes shared by Nuplane route tests.</summary>
internal static class NuplanePackageRootFixture
{
    public static string InstallInto(string root, string package, string version, bool complete)
    {
        var installPath = Path.Join(root, "local", package, version);
        var directory = Path.Join(installPath, "lib", "net10.0");
        Directory.CreateDirectory(directory);
        File.Copy(typeof(WidgetsDbContext).Assembly.Location, Path.Join(directory, $"{package}.dll"));
        if (complete)
            File.WriteAllText(Path.Join(installPath, NuplaneInstallRoot.ReadyMarker), "");
        return installPath;
    }

    /// <summary>
    /// Writes a <c>store-state.json</c> in the serialized shape expected by Nuplane's active-package mapper:
    /// one active version and matching descriptor. With no graph record, Nuplane groups this single package
    /// by the descriptor's default graph identity.
    /// </summary>
    public static void WriteStateFile(string root, string packageId, string version, string installPath)
    {
        const string timestamp = "2026-09-21T00:00:00Z";
        var json = JsonSerializer.Serialize(new
        {
            activeVersionById = new Dictionary<string, string> { [packageId] = version },
            lastKnownGoodById = new Dictionary<string, string>(),
            lastFailureById = new Dictionary<string, object>(),
            lastSuccessfulSourceSnapshots = new Dictionary<string, object>(),
            updatedAt = timestamp,
            activePackageDescriptorsById = new Dictionary<string, object>
            {
                [packageId] = new
                {
                    packageId,
                    version,
                    feedName = "local",
                    sourceName = "local",
                    installPath,
                    activatedAtUtc = timestamp,
                    activationCorrelationId = "test-correlation"
                }
            },
            activeGraphsById = new Dictionary<string, object>()
        });
        File.WriteAllText(Path.Join(root, NuplaneInstallRoot.StateFileName), json);
    }
}
