using Microsoft.Extensions.Configuration;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Reads a host's <c>Nuplane</c> section from <c>src/apps/&lt;host&gt;/appsettings.json</c> through the same JSON
/// configuration provider the host reads it with, so the <c>//</c> comments <c>Elsa.Foundation.Host</c> carries
/// parse as they do at runtime.
/// </summary>
internal static class NuplaneHostSettings
{
    /// <summary>
    /// Where a host keeps the <c>appsettings.{Environment}.json</c> files a build with computed package versions ships in
    /// place of its committed ones (#2084), relative to the host's directory.
    /// </summary>
    internal const string ComputedVersionsDirectory = "ComputedVersions";

    /// <summary>Every host under <c>src/apps</c> whose <c>appsettings.json</c> shares at least one assembly, in ordinal order.</summary>
    internal static IEnumerable<string> HostsThatShareAssemblies() =>
        Directory.EnumerateDirectories(Path.Join(RepoRoot, "src", "apps"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(host => File.Exists(HostFile(host, "appsettings.json")))
            .Where(host => SharedAssemblies(ReadNuplane(host)).Count > 0)
            .Order(StringComparer.Ordinal);

    internal static string HostDirectory(string host) => Path.Join(RepoRoot, "src", "apps", host);

    /// <summary>A file of the host's, from its path relative to the host's directory, <c>/</c>-separated.</summary>
    internal static string HostFile(string host, string relativePath) => Path.Join([HostDirectory(host), .. relativePath.Split('/')]);

    /// <summary>
    /// The host's <c>Nuplane</c> section as the host reads it with <paramref name="overlay"/> layered over its
    /// <c>appsettings.json</c>, the way an environment's <c>appsettings.{Environment}.json</c> is: arrays merge entry by
    /// entry, so an overlay's list replaces only as many entries as it has.
    /// </summary>
    /// <param name="overlay">A file relative to the host's directory, <c>/</c>-separated, or null for <c>appsettings.json</c> alone.</param>
    internal static IConfigurationSection ReadNuplane(string host, string? overlay = null)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(HostFile(host, "appsettings.json"));
        if (overlay is not null)
            builder.AddJsonFile(HostFile(host, overlay));

        return builder.Build().GetSection("Nuplane");
    }

    /// <summary>The names listed under <c>Loading:SharedAssemblies</c>.</summary>
    internal static IReadOnlyList<string> SharedAssemblies(IConfiguration nuplane) =>
    [
        .. nuplane.GetSection("Loading:SharedAssemblies").GetChildren()
            .Select(entry => entry["Name"])
            .OfType<string>()
    ];
}
