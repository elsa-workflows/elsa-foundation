using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
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
    /// Keep the Workbench as the shell supervisor's child on POSIX. The managed Process tracks the supervisor, which
    /// remains runnable while the Workbench is stopped, avoiding the .NET 10 macOS child-wait path for a stopped child.
    /// Arguments and paths are passed as positional arguments; the script never evaluates caller-provided text.
    /// </summary>
    private const string PosixSupervisorScript = """
        pid_file=$1
        shift
        workbench_pid=
        terminate() {
          trap - HUP INT TERM
          if [ -n "$workbench_pid" ]; then
            kill -KILL "$workbench_pid" 2>/dev/null
            wait "$workbench_pid" 2>/dev/null
          fi
          exit 143
        }
        trap terminate HUP INT TERM
        "$@" &
        workbench_pid=$!
        if ! printf '%s\n' "$workbench_pid" > "$pid_file"; then
          kill -KILL "$workbench_pid" 2>/dev/null
          wait "$workbench_pid" 2>/dev/null
          exit 125
        fi
        wait "$workbench_pid"
        status=$?
        trap - HUP INT TERM
        exit "$status"
        """;

    /// <summary>
    /// A ceiling for pathological hangs, not an expected duration: the host is ready in about ten seconds on a CI
    /// runner. Activation failures and early exits end the wait within seconds.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(10);

    private readonly Process _process;
    private readonly ProcessStartInfo _workbenchStartInfo;
    private readonly string _directory;
    private readonly string _managementKey;
    private readonly string _processIdPath;
    private bool _ownsDirectory = true;
    private bool _processStarted;
    private int? _workbenchProcessId;
    private bool _suspended;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<Uri> _listeningAddress = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WorkbenchProcess(Process process, ProcessStartInfo workbenchStartInfo, string directory, string managementKey)
    {
        _process = process;
        _workbenchStartInfo = workbenchStartInfo;
        _directory = directory;
        _managementKey = managementKey;
        _processIdPath = Path.Combine(directory, $".workbench-{Guid.NewGuid():N}.pid");
        Client = new HttpClient();
        ManagementClient = new HttpClient();
        ManagementClient.DefaultRequestHeaders.Add(ManagementKeyHeader, managementKey);
    }

    /// <summary>An anonymous client.</summary>
    public HttpClient Client { get; }

    /// <summary>A client carrying the host management key.</summary>
    public HttpClient ManagementClient { get; }

    /// <summary>The isolated authored configuration directory used by this process.</summary>
    public string ContentRoot => _directory;

    /// <summary>The operating-system process id owned by this fixture.</summary>
    public int ProcessId => _workbenchProcessId
        ?? throw new InvalidOperationException("The Workbench process id is not available before the host starts.");

    /// <summary>Whether the owned Workbench process has exited.</summary>
    public bool HasExited => !_processStarted || _process.HasExited;

    /// <summary>The POSIX shell supervisor id, exposed internally for the process-control smoke test.</summary>
    internal int SupervisorProcessId => _process.Id;

    public static async Task<WorkbenchProcess> StartAsync(
        WorkbenchShell shell,
        Action<string>? prepareContentRoot = null,
        bool waitForReady = true,
        IReadOnlyList<string>? additionalArguments = null)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-smoke-").FullName;
        CopySourceFile(shell.ShellFile, directory, "shells.json");
        if (shell.EnvironmentOverlay is not null)
            CopySourceFile(shell.EnvironmentOverlay, directory, $"shells.{shell.Environment}.json");
        CopySourceFile("appsettings.json", directory);
        if (File.Exists(WorkbenchBuild.SourceFile($"appsettings.{shell.Environment}.json")))
            CopySourceFile($"appsettings.{shell.Environment}.json", directory);
        Directory.CreateDirectory(Path.Combine(directory, "packages"));
        try
        {
            prepareContentRoot?.Invoke(directory);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }

        // Let Kestrel reserve its own ephemeral port. Selecting a "free" port with a temporary listener and
        // releasing it before the child binds leaves a race with other parallel test processes.
        const string requestedAddress = "http://127.0.0.1:0";
        var managementKey = Guid.NewGuid().ToString("n");
        var settings = new Dictionary<string, string>(shell.Settings)
        {
            ["Elsa:ModuleManagement:ApiKey"] = managementKey,
            // Nuplane keeps its store state beside the host binaries by default, which every run shares.
            ["Nuplane:Setup:StateFilePath"] = Path.Combine(directory, ".nuplane", "store-state.json")
        };

        var arguments = new List<string>
        {
            WorkbenchBuild.AssemblyPath(), "--contentRoot", directory, "--urls", requestedAddress
        };
        if (additionalArguments is not null)
            arguments.AddRange(additionalArguments);

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = shell.Environment;
        foreach (var (key, value) in settings)
            startInfo.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        // The child reports Kestrel's selected port through this lifetime message, even when a shell's settings
        // suppress other informational logs.
        startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";

        var workbench = new WorkbenchProcess(new Process(), startInfo, directory, managementKey);
        try
        {
            await workbench.StartAndWaitUntilReadyAsync(waitForReady);
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

    /// <summary>
    /// Asks the host to stop with SIGTERM, as a process manager does, and waits up to <paramref name="limit"/> for the
    /// process to exit. Returns how long the exit took, or <see langword="null"/> if the host was still running.
    /// </summary>
    /// <remarks>
    /// SIGTERM enters the same <c>ConsoleLifetime</c> handler as Ctrl+C. SIGINT is not used: a background job of a
    /// non-interactive shell inherits SIGINT as ignored, and a child process keeps that disposition. When the tests run
    /// under such a job, SIGINT would never arrive and a healthy host would look hung.
    /// </remarks>
    public async Task<TimeSpan?> StopAsync(TimeSpan limit)
    {
        var stopping = Stopwatch.StartNew();
        if (HasExited)
        {
            _suspended = false;
            return TimeSpan.Zero;
        }

        if (_suspended)
            await ResumeAsync();

        if (!await SendPosixSignalAsync("-TERM"))
            return stopping.Elapsed;

        try
        {
            // Also waits for the redirected output to reach its end, so Output holds every shutdown line.
            await _process.WaitForExitAsync().WaitAsync(limit);
            return stopping.Elapsed;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public int ExitCode => _process.ExitCode;

    /// <summary>Sends SIGSTOP and waits until the owned process is observed stopped. Supported on POSIX hosts only.</summary>
    public async Task SuspendAsync()
    {
        if (!await SendPosixSignalAsync("-STOP"))
            throw Failure($"exited with code {_process.ExitCode} before it could be suspended");

        _suspended = true;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        string state;
        do
        {
            using var query = Process.Start(new ProcessStartInfo("ps",
                ["-o", "state=", "-p", ProcessId.ToString(CultureInfo.InvariantCulture)])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            }) ?? throw Failure("could not start its process-state observation");
            state = (await query.StandardOutput.ReadToEndAsync()).Trim();
            await query.WaitForExitAsync();
            if (query.ExitCode != 0)
                throw Failure($"could not observe process {ProcessId} after SIGSTOP (ps exit {query.ExitCode})");
            if (state.StartsWith('T')) return;
            await Task.Delay(25);
        } while (DateTimeOffset.UtcNow < deadline);

        throw Failure($"process {ProcessId} did not stop after SIGSTOP; last state '{state}'");
    }

    /// <summary>Resumes the owned Workbench process with SIGCONT. Supported on POSIX hosts only.</summary>
    public async Task ResumeAsync()
    {
        await SendPosixSignalAsync("-CONT");
        _suspended = false;
    }

    /// <summary>Hard-kills the owned Workbench process and waits for it to exit; safe after an earlier exit.</summary>
    public async Task CrashAsync()
    {
        if (!_processStarted || HasExited)
        {
            if (_processStarted)
                await _process.WaitForExitAsync();
            _suspended = false;
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (_process.HasExited)
            {
                // The child exited between the check and Kill.
            }
            catch (System.ComponentModel.Win32Exception) when (_process.HasExited)
            {
                // The child exited between the check and Kill.
            }
        }
        else if (_workbenchProcessId is int processId)
        {
            await SendPosixSignalAsync("-KILL", processId);
        }
        else
        {
            // Before its pid file is available, the supervisor's TERM trap kills and reaps only its owned child.
            await SendPosixSignalAsync("-TERM", _process.Id);
        }

        await _process.WaitForExitAsync();
        _suspended = false;
    }

    /// <summary>Kills this owned host without graceful cleanup, then boots a fresh process over its existing databases.</summary>
    public async Task<WorkbenchProcess> CrashAndRestartAsync(Action<string>? prepareRestart = null)
    {
        await CrashAsync();
        prepareRestart?.Invoke(_directory);
        var replacement = new WorkbenchProcess(new Process(), _workbenchStartInfo, _directory, _managementKey);
        _ownsDirectory = false;
        try
        {
            await replacement.StartAndWaitUntilReadyAsync(waitForReady: true);
            return replacement;
        }
        catch
        {
            await replacement.DisposeAsync();
            throw;
        }
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
        await CrashAsync();
        _process.Dispose();
        try
        {
            if (_ownsDirectory)
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is harmless; failing the test over it is not.
        }
    }

    private async Task<bool> SendPosixSignalAsync(string signal)
        => await SendPosixSignalAsync(signal, ProcessId);

    private async Task<bool> SendPosixSignalAsync(string signal, int processId)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Workbench process signals are supported only on POSIX hosts.");

        if (!_processStarted || _process.HasExited)
        {
            _suspended = false;
            return false;
        }

        using var signalProcess = Process.Start("kill", [signal, processId.ToString(CultureInfo.InvariantCulture)])
                                  ?? throw new InvalidOperationException("Could not start the POSIX kill command.");
        await signalProcess.WaitForExitAsync();
        if (signalProcess.ExitCode == 0)
            return true;

        if (_process.HasExited)
            return false;

        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
            return false;
        }
        catch (TimeoutException)
        {
            // The owned Workbench and its supervisor are still active; report the failed signal below.
        }

        throw new InvalidOperationException(
            $"Could not send {signal} to owned process {processId}: kill exited with code {signalProcess.ExitCode}.");
    }

    private async Task StartAndWaitUntilReadyAsync(bool waitForReady)
    {
        _process.StartInfo = OperatingSystem.IsWindows()
            ? _workbenchStartInfo
            : CreatePosixSupervisorStartInfo(_workbenchStartInfo, _processIdPath);
        _process.OutputDataReceived += (_, line) => Append(line.Data);
        _process.ErrorDataReceived += (_, line) => Append(line.Data);
        _process.Start();
        _processStarted = true;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (OperatingSystem.IsWindows())
            _workbenchProcessId = _process.Id;
        else
            await WaitForWorkbenchProcessIdAsync();

        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (true)
        {
            if (_process.HasExited)
                throw Failure($"exited with code {_process.ExitCode} before the default shell was ready");

            if (Client.BaseAddress is null && _listeningAddress.Task.IsCompletedSuccessfully)
            {
                Client.BaseAddress = await _listeningAddress.Task;
                ManagementClient.BaseAddress = Client.BaseAddress;
            }

            var readiness = Client.BaseAddress is null ? null : await TryReadReadinessAsync();
            if (!waitForReady && readiness is not null)
                return;
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

    private static ProcessStartInfo CreatePosixSupervisorStartInfo(ProcessStartInfo workbenchStartInfo, string processIdPath)
    {
        var supervisor = new ProcessStartInfo("/bin/sh")
        {
            WorkingDirectory = workbenchStartInfo.WorkingDirectory,
            RedirectStandardOutput = workbenchStartInfo.RedirectStandardOutput,
            RedirectStandardError = workbenchStartInfo.RedirectStandardError,
            UseShellExecute = false
        };
        supervisor.ArgumentList.Add("-c");
        supervisor.ArgumentList.Add(PosixSupervisorScript);
        supervisor.ArgumentList.Add("elsa-workbench-supervisor");
        supervisor.ArgumentList.Add(processIdPath);
        supervisor.ArgumentList.Add(workbenchStartInfo.FileName);
        foreach (var argument in workbenchStartInfo.ArgumentList)
            supervisor.ArgumentList.Add(argument);
        foreach (var (key, value) in workbenchStartInfo.Environment)
            supervisor.Environment[key] = value;

        return supervisor;
    }

    private async Task WaitForWorkbenchProcessIdAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(_processIdPath))
            {
                var value = (await File.ReadAllTextAsync(_processIdPath)).Trim();
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) && processId > 0)
                {
                    _workbenchProcessId = processId;
                    return;
                }
            }

            if (_process.HasExited)
                throw Failure($"supervisor exited with code {_process.ExitCode} before reporting its Workbench process id");
            await Task.Delay(10);
        }

        throw Failure("supervisor did not report its Workbench process id within 10 seconds");
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

        const string marker = "Now listening on: ";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index >= 0 &&
            Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) &&
            address.Host == "127.0.0.1" && address.Port > 0)
            _listeningAddress.TrySetResult(address);
    }

    private static void CopySourceFile(string fileName, string directory, string? targetName = null) =>
        File.Copy(WorkbenchBuild.SourceFile(fileName), Path.Combine(directory, targetName ?? fileName));

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
