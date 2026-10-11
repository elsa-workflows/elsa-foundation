using System.Net;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The built <c>Elsa.Foundation.Host</c> running as a child process, the way an operator runs it. This facade keeps the
/// existing Foundation host test API while sharing process ownership with other real-app process tests.
/// </summary>
internal sealed class FoundationHostProcess : IAsyncDisposable
{
    private const string Host = "Elsa.Foundation.Host";
    private static readonly string SourceDirectory = Path.Join(RepoRoot, "src", "apps", Host);
    private readonly ChildHostProcess _process;

    /// <summary>The header the host's module-management endpoints read their key from.</summary>
    public const string ModuleManagementKeyHeader = ChildHostProcess.ModuleManagementKeyHeader;

    private FoundationHostProcess(ChildHostProcess process) => _process = process;

    /// <summary>The host's console output so far, for assertion messages.</summary>
    public string Output => _process.Output;

    /// <summary>Where Nuplane extracts the packages this host acquires, one directory per package.</summary>
    public string PackageInstallRoot => _process.PackageInstallRoot;

    /// <summary>Starts the host over a package directory and returns once its shells are active, unless <paramref name="awaitShells"/> is false.</summary>
    public static Task<FoundationHostProcess> StartAsync(string shells, string packages, IReadOnlyDictionary<string, string> settings, bool awaitShells = true) =>
        StartAsync(shells, Directory.EnumerateFiles(packages, "*.nupkg"), settings, awaitShells: awaitShells);

    /// <summary>
    /// Starts the host over exactly <paramref name="packageFiles"/>. With <paramref name="deployed"/>, it runs from a copy
    /// of its build output that is also its content root, as an operator's published host does. With <paramref name="awaitShells"/>
    /// false, it returns once the host listens.
    /// </summary>
    public static async Task<FoundationHostProcess> StartAsync(string shells, IEnumerable<string> packageFiles, IReadOnlyDictionary<string, string> settings, bool deployed = false, bool awaitShells = true)
    {
        var process = await ChildHostProcess.StartAsync(
            Host, SourceDirectory, "elsa-foundation-host-boot-", shells, packageFiles, settings, deployed, awaitShells);
        return new FoundationHostProcess(process);
    }

    /// <summary>The host's private content root.</summary>
    public string ContentRoot => _process.ContentRoot;

    /// <summary>The directory the host's feed reads.</summary>
    public string PackagesDirectory => _process.PackagesDirectory;

    /// <summary>Whether the process started for this test is still running.</summary>
    public bool IsRunning => _process.IsRunning;

    /// <summary>The operating-system process id, stable for this owned process lifetime.</summary>
    public int ProcessId => _process.ProcessId;

    /// <summary>Replaces all feed releases of <paramref name="packageId"/> with <paramref name="package"/> using a staged move.</summary>
    public void UpgradeInPlace(string packageId, string package) => _process.UpgradeInPlace(packageId, package);

    /// <summary>Returns release packages matching <c>{id}.{version}.nupkg</c> in <paramref name="directory"/>.</summary>
    public static IEnumerable<string> Releases(string directory, string packageId) => ChildHostProcess.Releases(directory, packageId);

    /// <summary>The packages Nuplane has active in this host, read from its private store state.</summary>
    public Task<IReadOnlyDictionary<string, string>> ActivePackagesAsync() => _process.ActivePackagesAsync();

    /// <summary>The managed DLL paths reported as mapped by this process.</summary>
    public Task<IReadOnlyList<string>> MappedAssembliesAsync() => _process.MappedAssembliesAsync();

    /// <summary>The status and body returned by a GET request.</summary>
    public Task<(HttpStatusCode Status, string Body)> GetAsync(string path) => _process.GetAsync(path);

    /// <summary>The status and body returned by an authenticated module-management POST request.</summary>
    public Task<(HttpStatusCode Status, string Body)> PostModuleManagementAsync(string path, string? key, TimeSpan? timeout = null) =>
        _process.PostModuleManagementAsync(path, key, timeout);

    /// <summary>Stops the process tree and deletes the owned content root.</summary>
    public ValueTask DisposeAsync() => _process.DisposeAsync();

    public static string RepoRoot => ChildHostProcess.RepoRoot;

    /// <summary>The build configuration shared by the host and package fixtures.</summary>
    public static string Configuration => ChildHostProcess.Configuration;

    /// <summary>The dotnet host used by the test process.</summary>
    public static string DotnetPath => ChildHostProcess.DotnetPath;
}
