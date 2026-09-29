using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The built <c>Elsa.Foundation.Host</c> running as a child process, the way an operator runs it: a content root of its
/// own holding its <c>appsettings.json</c>, a <c>shells.json</c> the test authors and a <c>packages</c> directory feed,
/// and every other setting an environment variable. Nothing of the host is loaded into the test process, so the module
/// the host loads from the feed reaches the host's membership by the types the host really shares (#2143).
/// </summary>
internal sealed class FoundationHostProcess : IAsyncDisposable
{
    private const string Host = "Elsa.Foundation.Host";

    /// <summary>A ceiling for pathological hangs: the host reconciles its feed and activates its shell in seconds.</summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(5);

    private readonly Process _process;
    private readonly string _contentRoot;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HttpClient _client = new();

    private FoundationHostProcess(Process process, string contentRoot)
    {
        _process = process;
        _contentRoot = contentRoot;
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

    /// <summary>
    /// Starts the host over <paramref name="packages"/>, a directory of <c>.nupkg</c> files it takes as its feed, with
    /// <paramref name="shells"/> as its <c>shells.json</c> and <paramref name="settings"/> as environment variables, and
    /// returns once its shells are active.
    /// </summary>
    public static async Task<FoundationHostProcess> StartAsync(string shells, string packages, IReadOnlyDictionary<string, string> settings)
    {
        var contentRoot = Directory.CreateTempSubdirectory("elsa-foundation-host-boot-").FullName;
        try
        {
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
                File.Copy(Path.Join(SourceDirectory, file), Path.Join(contentRoot, file));
            File.WriteAllText(Path.Join(contentRoot, "shells.json"), shells);
            var feed = Directory.CreateDirectory(Path.Join(contentRoot, "packages")).FullName;
            foreach (var package in Directory.EnumerateFiles(packages, "*.nupkg"))
                File.Copy(package, Path.Join(feed, Path.GetFileName(package)));

            // Kestrel reserves its own ephemeral port: picking a free one and releasing it races other test processes.
            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                [AssemblyPath, "--contentRoot", contentRoot, "--urls", "http://127.0.0.1:0"])
            {
                WorkingDirectory = contentRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            // Nuplane keeps its store state and the packages it installs beside the host binaries by default, which every
            // run would share.
            startInfo.Environment["Nuplane__Setup__StateFilePath"] = Path.Join(contentRoot, ".nuplane", "store-state.json");
            startInfo.Environment["Nuplane__FeedResolution__PackageInstallRoot"] = Path.Join(contentRoot, ".nuplane", "packages");
            startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            foreach (var (key, value) in settings)
                startInfo.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;

            var host = new FoundationHostProcess(new Process { StartInfo = startInfo }, contentRoot);
            try
            {
                await host.StartAndWaitUntilReadyAsync();
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }
        catch
        {
            if (Directory.Exists(contentRoot))
                Directory.Delete(contentRoot, recursive: true);
            throw;
        }
    }

    /// <summary>The status a request to <paramref name="path"/> is answered with, and its body.</summary>
    public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
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
                throw Failure($"exited with code {_process.ExitCode} before its shells were active");
            if (_client.BaseAddress is null && _listening.Task.IsCompletedSuccessfully)
                _client.BaseAddress = await _listening.Task;
            if (_client.BaseAddress is not null && await IsReadyAsync())
                return;
            if (DateTimeOffset.UtcNow > deadline)
                throw Failure($"did not activate its shells within {ReadyTimeout}");

            await Task.Delay(250);
        }
    }

    private async Task<bool> IsReadyAsync()
    {
        try
        {
            using var response = await _client.GetAsync("/health/ready");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            return false; // Not listening yet.
        }
    }

    private InvalidOperationException Failure(string reason) => new($"{Host} {reason}. Host output:{Environment.NewLine}{Output}");

    private void Append(string? line)
    {
        if (line is null)
            return;

        lock (_output)
            _output.AppendLine(line);

        const string marker = "Now listening on: ";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index >= 0 && Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) && address.Port > 0)
            _listening.TrySetResult(address);
    }

    private static string SourceDirectory => Path.Join(RepoRoot, "src", "apps", Host);

    /// <summary>
    /// <c>Elsa.Foundation.Host.dll</c> from the host's own <c>bin</c> folder, built with this assembly's configuration and
    /// target framework. The build-order project reference guarantees it exists.
    /// </summary>
    private static string AssemblyPath
    {
        get
        {
            var configuration = typeof(FoundationHostProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var path = Path.Join(SourceDirectory, "bin", configuration, framework, Host + ".dll");
            return File.Exists(path) ? path : throw new FileNotFoundException($"Build src/apps/{Host} ({configuration}) before running these tests.", path);
        }
    }

    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Elsa.Server.slnx above {AppContext.BaseDirectory}.");
    }
}
