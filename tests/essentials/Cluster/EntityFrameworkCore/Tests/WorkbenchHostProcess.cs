using System.Net;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>The built Workbench running as an isolated child process with the same lifecycle helper as Foundation.Host.</summary>
internal sealed class WorkbenchHostProcess : IAsyncDisposable
{
    private const string Host = "Elsa.Workbench";
    private static readonly string SourceDirectory = Path.Join(ChildHostProcess.RepoRoot, "src", "apps", Host);
    private readonly ChildHostProcess _process;

    /// <summary>The management-key header used by Workbench's module and shell-management endpoints.</summary>
    public const string ModuleManagementKeyHeader = ChildHostProcess.ModuleManagementKeyHeader;

    private WorkbenchHostProcess(ChildHostProcess process) => _process = process;

    public string Output => _process.Output;
    public string ContentRoot => _process.ContentRoot;
    public string PackagesDirectory => _process.PackagesDirectory;
    public string PackageInstallRoot => _process.PackageInstallRoot;
    public bool IsRunning => _process.IsRunning;
    public int ProcessId => _process.ProcessId;

    /// <summary>The app's normal shell configuration, copied into the process-owned content root by <see cref="StartAsync"/>.</summary>
    public static string DefaultShellsJson => File.ReadAllText(Path.Join(SourceDirectory, "shells.json"));

    /// <summary>Starts Workbench over exactly <paramref name="packageFiles"/> with its shell JSON and environment overrides.</summary>
    public static async Task<WorkbenchHostProcess> StartAsync(string shells, IEnumerable<string> packageFiles, IReadOnlyDictionary<string, string> settings, bool awaitShells = true)
    {
        var process = await ChildHostProcess.StartAsync(
            Host, SourceDirectory, "elsa-workbench-host-", shells, packageFiles, settings, awaitShells: awaitShells);
        return new WorkbenchHostProcess(process);
    }

    public void UpgradeInPlace(string packageId, string package) => _process.UpgradeInPlace(packageId, package);
    public Task<IReadOnlyDictionary<string, string>> ActivePackagesAsync() => _process.ActivePackagesAsync();
    public Task<IReadOnlyList<string>> MappedAssembliesAsync() => _process.MappedAssembliesAsync();
    public Task<(HttpStatusCode Status, string Body)> GetAsync(string path) => _process.GetAsync(path);
    public Task<(HttpStatusCode Status, string Body)> GetManagementAsync(string path, string? key) => _process.GetManagementAsync(path, key);
    public Task<(HttpStatusCode Status, string Body)> PostAsync(string path, string? key, TimeSpan? timeout = null) =>
        _process.PostModuleManagementAsync(path, key, timeout);
    public ValueTask DisposeAsync() => _process.DisposeAsync();
}
