using Microsoft.Extensions.Configuration;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Reads a host's <c>Nuplane</c> section from <c>src/apps/&lt;host&gt;/appsettings.json</c> through the same JSON
/// configuration provider the host reads it with, so the <c>//</c> comments <c>Elsa.Foundation.Host</c> carries
/// parse as they do at runtime.
/// </summary>
internal static class NuplaneHostSettings
{
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Every host under <c>src/apps</c> whose <c>appsettings.json</c> shares at least one assembly, in ordinal order.</summary>
    internal static IEnumerable<string> HostsThatShareAssemblies() =>
        Directory.EnumerateDirectories(Path.Join(RepoRoot, "src", "apps"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(host => File.Exists(Path.Join(RepoRoot, "src", "apps", host, "appsettings.json")))
            .Where(host => SharedAssemblies(ReadNuplane(host)).Count > 0)
            .Order(StringComparer.Ordinal);

    internal static IConfigurationSection ReadNuplane(string host) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Join(RepoRoot, "src", "apps", host, "appsettings.json"))
            .Build()
            .GetSection("Nuplane");

    /// <summary>The names listed under <c>Loading:SharedAssemblies</c>.</summary>
    internal static IReadOnlyList<string> SharedAssemblies(IConfiguration nuplane) =>
    [
        .. nuplane.GetSection("Loading:SharedAssemblies").GetChildren()
            .Select(entry => entry["Name"])
            .OfType<string>()
    ];

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
