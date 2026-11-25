using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace Elsa.Studio.Tests;

/// <summary>
/// A built Studio app under <c>Studio/src/apps</c> as a child process, the way an operator runs it in Development: its committed
/// <c>appsettings.json</c>, <c>shells.json</c> and <c>nuplane-management.json</c> in a content root of its own, with an empty
/// <c>packages</c> feed. Nothing of the app is loaded into the test process (#5768).
/// </summary>
public abstract class StudioAppProcess(string name, IReadOnlyDictionary<string, string>? environment = null) : IAsyncLifetime
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly StringBuilder _output = new();
    private TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private string? _contentRoot;
    private string? _scratch;

    /// <summary>The app's project name, which is also its folder under <c>Studio/src/apps</c>.</summary>
    public string Name { get; } = name;

    /// <summary>The client every request to the app goes through; its base address is the app's listening URL.</summary>
    public HttpClient Client { get; private set; } = new();

    /// <summary>The app's own content root, holding its copied configuration and the <c>packages</c> feed.</summary>
    public string ContentRoot => _contentRoot ?? throw new InvalidOperationException($"{Name} has not started.");

    /// <summary>A directory of the test's own beside the content root, for files the app must not see; deleted with the host.</summary>
    public string ScratchDirectory => _scratch ??= Directory.CreateTempSubdirectory($"{Name.ToLowerInvariant()}-scratch-").FullName;

    /// <summary>The app's process id, so a test can show the app was never restarted.</summary>
    public int ProcessId => _process?.Id ?? throw new InvalidOperationException($"{Name} has not started.");

    /// <summary>The app's build output directory.</summary>
    public string OutputDirectory => Path.Join(SourceDirectory, "bin", Configuration, Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)));

    /// <summary>The app's project directory.</summary>
    public string SourceDirectory => Path.Join(StudioRoot, "src", "apps", Name);

    /// <summary>The host's console output so far, for assertion messages.</summary>
    public string OutputSection
    {
        get
        {
            lock (_output)
                return $"{Environment.NewLine}Host output:{Environment.NewLine}{_output}";
        }
    }

    public async Task InitializeAsync()
    {
        var assembly = Path.Join(OutputDirectory, Name + ".dll");
        if (!File.Exists(assembly))
            throw new FileNotFoundException($"Build {Name} ({Configuration}) before running these tests.", assembly);

        _contentRoot = CreateContentRoot();
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json", "shells.json", "nuplane-management.json" })
            File.Copy(Path.Join(SourceDirectory, file), Path.Join(_contentRoot, file));
        Directory.CreateDirectory(Path.Join(_contentRoot, "packages"));
        PrepareContentRoot(_contentRoot);
        await StartAsync(assembly);
    }

    /// <summary>Stops the app and starts it again over the same content root, as an operator's restart does.</summary>
    public async Task RestartAsync()
    {
        await StopAsync();
        _process?.Dispose();
        // The app listens on a new port, and a client's base address is fixed once it has sent a request.
        Client.Dispose();
        Client = new();
        _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartAsync(Path.Join(OutputDirectory, Name + ".dll"));
    }

    /// <summary>Runs once the content root holds the app's configuration and its empty feed, before the first start.</summary>
    protected virtual void PrepareContentRoot(string contentRoot)
    {
    }

    private async Task StartAsync(string assembly)
    {
        // Kestrel reserves its own ephemeral port: picking a free one and releasing it races other test processes.
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [assembly, "--contentRoot", ContentRoot, "--urls", "http://127.0.0.1:0"])
        {
            WorkingDirectory = ContentRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[key] = value;
        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, line) => Append(line.Data);
        _process.ErrorDataReceived += (_, line) => Append(line.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (true)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"{Name} exited with code {_process.ExitCode} before it listened.{OutputSection}");
            if (_listening.Task.IsCompletedSuccessfully)
            {
                Client.BaseAddress = await _listening.Task;
                return;
            }
            if (DateTimeOffset.UtcNow > deadline)
                throw new InvalidOperationException($"{Name} did not listen within {ReadyTimeout}.{OutputSection}");

            await Task.Delay(250);
        }
    }

    /// <summary>The status, media type and body a <c>GET</c> of <paramref name="path"/> accepting <paramref name="accept"/> is answered with.</summary>
    public async Task<(HttpStatusCode Status, string? MediaType, string Body)> GetAsync(string path, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        using var response = await Client.SendAsync(request);
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        try
        {
            await StopAsync();
        }
        finally
        {
            _process?.Dispose();
            if (_contentRoot is not null)
                TryDelete(_contentRoot);
            if (_scratch is not null)
                TryDelete(_scratch);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            if (_process is { HasExited: false })
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
    }

    /// <summary>
    /// A fresh content root. The shipped apps enroll their package store there, and Nuplane qualifies ext4 on Linux, so on a
    /// runner the root goes on the workspace volume instead of an image-dependent <c>/tmp</c> mount, as for the native-store
    /// administration fixture.
    /// </summary>
    private string CreateContentRoot()
    {
        var workspace = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        return OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(workspace)
            ? Directory.CreateDirectory(Path.Join(workspace, $"{Name.ToLowerInvariant()}-{Guid.NewGuid():N}")).FullName
            : Directory.CreateTempSubdirectory($"{Name.ToLowerInvariant()}-").FullName;
    }

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

    /// <summary>A temp directory left behind is harmless; failing the test over it, or masking an earlier failure, is not.</summary>
    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The build configuration this assembly was built with, which the app is built in too.</summary>
    public static string Configuration { get; } = typeof(StudioAppProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

    /// <summary>The <c>Studio</c> folder of this checkout.</summary>
    public static string StudioRoot { get; } = FindStudioRoot();

    private static string FindStudioRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Studio.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Elsa.Studio.slnx above {AppContext.BaseDirectory}.");
    }
}
