using System.Diagnostics;
using System.Net;
using System.ComponentModel;
using System.Reflection;

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

    /// <summary>How long a killed host gets to be gone before disposal stops waiting.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly string _contentRoot;
    private readonly CapturedOutput _output = new();
    private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HttpClient _client = new();
    private bool _started;

    private FoundationHostProcess(Process process, string contentRoot)
    {
        _process = process;
        _contentRoot = contentRoot;
    }

    /// <summary>The host's console output so far, for assertion messages.</summary>
    public string Output => _output.ToString();

    /// <summary>
    /// Starts the host over <paramref name="packages"/>, a directory of <c>.nupkg</c> files it takes as its feed, with
    /// <paramref name="shells"/> as its <c>shells.json</c> and <paramref name="settings"/> as environment variables, and
    /// returns once its shells are active, or, when <paramref name="untilReady"/> is <see langword="false"/> because they
    /// activate only on their first request, once it is live.
    /// </summary>
    public static async Task<FoundationHostProcess> StartAsync(string shells, string packages, IReadOnlyDictionary<string, string> settings, bool untilReady = true)
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
            var startInfo = new ProcessStartInfo(DotnetPath,
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
                await host.StartAndWaitUntilAsync(untilReady ? "/health/ready" : "/health/live");
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
            TryDelete(contentRoot);
            throw;
        }
    }

    /// <summary>
    /// Upgrades a package in place, the way an operator does on a running host: every release of
    /// <paramref name="packageId"/> leaves the host's feed folder and <paramref name="package"/> takes its place, written
    /// under another name and moved in, so the folder's watcher never reads half a file. The host is not restarted.
    /// </summary>
    public void UpgradeInPlace(string packageId, string package)
    {
        var feed = Path.Join(_contentRoot, "packages");
        foreach (var previous in Releases(feed, packageId).ToArray())
            File.Delete(previous);

        var staged = Path.Join(feed, Path.GetFileName(package) + ".partial");
        File.Copy(package, staged);
        File.Move(staged, Path.Join(feed, Path.GetFileName(package)));
    }

    /// <summary>
    /// The <c>.nupkg</c> files in <paramref name="directory"/> that are a release of <paramref name="packageId"/>, whatever
    /// their version, and not of a longer id it prefixes: <c>{id}.{version}.nupkg</c>, whose version starts with a digit.
    /// </summary>
    public static IEnumerable<string> Releases(string directory, string packageId) =>
        Directory.EnumerateFiles(directory, $"{packageId}.*.nupkg")
            .Where(file => char.IsAsciiDigit(Path.GetFileName(file)[packageId.Length + 1]));

    /// <summary>Whether the host process is still the one <see cref="StartAsync"/> started, and still running.</summary>
    public bool IsRunning => _started && !_process.HasExited;

    /// <summary>Posts nothing to <paramref name="path"/> with <paramref name="headers"/>, and returns the status it is answered with.</summary>
    public async Task<HttpStatusCode> PostAsync(string path, IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        foreach (var (name, value) in headers)
            request.Headers.Add(name, value);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>The host's output from the first line that contains <paramref name="marker"/> on, or empty before that line.</summary>
    public string OutputSince(string marker)
    {
        var output = Output;
        var index = output.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "" : output[index..];
    }

    /// <summary>The status a request to <paramref name="path"/> is answered with, and its body.</summary>
    public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Stops the host's process tree if it is running. Safe when the process never started or has already exited, so it
    /// never masks the error that made a start fail, and it cannot wait for ever on a process that will not die.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        try
        {
            if (_started && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(StopTimeout);
                await _process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            // Exited between the check and the kill, or would not die in time: nothing left here to act on.
        }
        finally
        {
            _process.Dispose();
            TryDelete(_contentRoot);
        }
    }

    /// <summary>A temp directory left behind is harmless; failing the test over it, or masking an earlier failure, is not.</summary>
    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task StartAndWaitUntilAsync(string probe)
    {
        _process.OutputDataReceived += (_, line) => Append(line.Data);
        _process.ErrorDataReceived += (_, line) => Append(line.Data);
        _process.Start();
        _started = true;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (true)
        {
            if (_process.HasExited)
                throw Failure($"exited with code {_process.ExitCode} before {probe} answered");
            if (_client.BaseAddress is null && _listening.Task.IsCompletedSuccessfully)
                _client.BaseAddress = await _listening.Task;
            if (_client.BaseAddress is not null && await AnswersAsync(probe))
                return;
            if (DateTimeOffset.UtcNow > deadline)
                throw Failure($"did not answer {probe} within {ReadyTimeout}");

            await Task.Delay(250);
        }
    }

    private async Task<bool> AnswersAsync(string probe)
    {
        try
        {
            using var response = await _client.GetAsync(probe);
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
        _output.Append(line);
        if (line is null)
            return;

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
            var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var path = Path.Join(SourceDirectory, "bin", Configuration, framework, Host + ".dll");
            return File.Exists(path) ? path : throw new FileNotFoundException($"Build src/apps/{Host} ({Configuration}) before running these tests.", path);
        }
    }

    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The build configuration this assembly was built with, which the host and the packages are built in too.</summary>
    public static string Configuration { get; } = typeof(FoundationHostProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

    /// <summary>The <c>dotnet</c> that is running the tests, so a child runs on the same SDK and runtime.</summary>
    public static string DotnetPath { get; } = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

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
