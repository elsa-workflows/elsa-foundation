using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;

namespace Elsa.Workbench.Tests;

/// <summary>
/// The built Elsa.Workbench running as a child process, the way an operator runs it: its own working directory holding
/// the shell files under test, and settings supplied as environment variables. Every instance gets a fresh directory,
/// so its SQLite files, file-lock folder, and Nuplane store state never meet another run's.
/// </summary>
public sealed class WorkbenchProcess : IAsyncDisposable
{
    public const string ManagementKeyHeader = "X-Elsa-Module-Management-Key";

    /// <summary>What <c>DefaultShellWarmup</c> logs, with the exception, when the default shell fails to activate.</summary>
    private const string ActivationFailureLog = "Default shell preparation failed";

    /// <summary>
    /// A ceiling for pathological hangs, not an expected duration: the host is ready in about ten seconds on a CI
    /// runner. Activation failures and early exits end the wait within seconds.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(10);

    private readonly Process _process;
    private readonly string _directory;
    private readonly StringBuilder _output = new();

    private WorkbenchProcess(Process process, string directory, Uri baseAddress, string managementKey)
    {
        _process = process;
        _directory = directory;
        Client = new HttpClient { BaseAddress = baseAddress };
        ManagementClient = new HttpClient { BaseAddress = baseAddress };
        ManagementClient.DefaultRequestHeaders.Add(ManagementKeyHeader, managementKey);
    }

    /// <summary>An anonymous client.</summary>
    public HttpClient Client { get; }

    /// <summary>A client carrying the host management key.</summary>
    public HttpClient ManagementClient { get; }

    public static async Task<WorkbenchProcess> StartAsync(WorkbenchShell shell)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-smoke-").FullName;
        CopySourceFile(shell.ShellFile, directory, "shells.json");
        if (shell.EnvironmentOverlay is not null)
            CopySourceFile(shell.EnvironmentOverlay, directory, $"shells.{shell.Environment}.json");
        CopySourceFile("appsettings.json", directory);
        if (File.Exists(WorkbenchBuild.SourceFile($"appsettings.{shell.Environment}.json")))
            CopySourceFile($"appsettings.{shell.Environment}.json", directory);
        Directory.CreateDirectory(Path.Combine(directory, "packages"));

        var baseAddress = new Uri($"http://127.0.0.1:{FreePort()}");
        var managementKey = Guid.NewGuid().ToString("n");
        var settings = new Dictionary<string, string>(shell.Settings)
        {
            ["Elsa:ModuleManagement:ApiKey"] = managementKey,
            // Nuplane keeps its store state beside the host binaries by default, which every run shares.
            ["Nuplane:Setup:StateFilePath"] = Path.Combine(directory, ".nuplane", "store-state.json")
        };

        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [WorkbenchBuild.AssemblyPath(), "--contentRoot", directory, "--urls", baseAddress.ToString()])
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = shell.Environment;
        foreach (var (key, value) in settings)
            startInfo.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;

        var workbench = new WorkbenchProcess(new Process { StartInfo = startInfo }, directory, baseAddress, managementKey);
        try
        {
            await workbench.StartAndWaitUntilReadyAsync();
            return workbench;
        }
        catch
        {
            await workbench.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<CatalogFeature>> ReadFeatureCatalogAsync()
    {
        var registry = await ManagementClient.GetFromJsonAsync<ModuleRegistry>("/_elsa/module-management/registry");
        return registry!.Modules.Single(module => module.Id == "Elsa.Workbench").Features;
    }

    /// <summary>The host's console output so far, for assertion messages.</summary>
    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        ManagementClient.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is harmless; failing the test over it is not.
        }
    }

    private async Task StartAndWaitUntilReadyAsync()
    {
        _process.OutputDataReceived += (_, line) => Append(line.Data);
        _process.ErrorDataReceived += (_, line) => Append(line.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (true)
        {
            if (_process.HasExited)
                throw Failure($"exited with code {_process.ExitCode} before the default shell was ready");

            var readiness = await TryReadReadinessAsync();
            if (readiness?.Status == "ready")
                return;
            if (readiness?.Status == "failed")
            {
                if (readiness.Code == "shell_activation_failed")
                    await WaitForActivationFailureLogAsync();
                throw Failure($"reported the default shell as failed ({readiness.Code})");
            }
            if (DateTimeOffset.UtcNow > deadline)
                throw Failure($"did not report the default shell ready within {ReadyTimeout}");

            await Task.Delay(250);
        }
    }

    private async Task<Readiness?> TryReadReadinessAsync()
    {
        try
        {
            using var response = await Client.GetAsync("/health/ready");
            return await response.Content.ReadFromJsonAsync<Readiness>();
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            return null; // Not listening, or not routing the readiness endpoint, yet.
        }
    }

    /// <summary>
    /// Readiness reports the failure just before the warmup logs why, and the console logger writes from its own
    /// queue, so wait (bounded) until the logged failure has reached the captured output and stopped growing.
    /// </summary>
    private async Task WaitForActivationFailureLogAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        var previousLength = -1;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var output = Output;
            if (output.Contains(ActivationFailureLog, StringComparison.Ordinal) && output.Length == previousLength)
                return;

            previousLength = output.Length;
            await Task.Delay(250);
        }
    }

    private InvalidOperationException Failure(string reason) =>
        new($"The Workbench {reason}. Host output:{Environment.NewLine}{Output}");

    private void Append(string? line)
    {
        if (line is null)
            return;

        lock (_output)
            _output.AppendLine(line);
    }

    private static void CopySourceFile(string fileName, string directory, string? targetName = null) =>
        File.Copy(WorkbenchBuild.SourceFile(fileName), Path.Combine(directory, targetName ?? fileName));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record Readiness(string Status, string? Code);

    private sealed record ModuleRegistry(IReadOnlyList<RegistryModule> Modules);

    private sealed record RegistryModule(string Id, IReadOnlyList<CatalogFeature> Features);
}

/// <summary>
/// A feature-catalog entry as the host's module-management registry reports it. <see cref="SourceKind"/> is
/// <c>runtime</c> for a feature class the host loaded; a name known only from shell configuration is reported with
/// source kind <c>shell</c>, and still as enabled.
/// </summary>
public sealed record CatalogFeature(string Id, string SourceKind, bool Enabled, Dictionary<string, System.Text.Json.JsonElement> Configuration)
{
    public bool Runs => Enabled && SourceKind == "runtime";
}
