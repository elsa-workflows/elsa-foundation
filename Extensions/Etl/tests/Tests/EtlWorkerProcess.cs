using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

/// <summary>Owns only a fixture child; shared files belong to RuntimeFiles and outlive every child.</summary>
internal sealed class EtlWorkerProcess(string root, string configurationPath, string role) : IAsyncDisposable
{
    private readonly Process _process = new();
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task[] _pumps = [];
    private bool _started;
    public int ProcessId => _process.Id;
    public bool HasExited => !_started || _process.HasExited;
    public string AcceptancePath => EtlWorkerProtocol.AcceptancePath(root, role);
    public string ReleasePath => EtlWorkerProtocol.ReleasePath(root, role);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        EtlWorkerProtocol.ValidateRole(role);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(HostAssemblyPath());
        start.ArgumentList.Add(configurationPath);
        start.ArgumentList.Add(role);
        _process.StartInfo = start;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => _ready.TrySetException(new InvalidOperationException($"ETL worker {role} exited before readiness. {Output}"));
        if (!_process.Start())
            throw new InvalidOperationException("Could not start the fixture ETL worker.");
        _started = true;
        _pumps = [PumpAsync(_process.StandardOutput), PumpAsync(_process.StandardError)];
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
    }

    public Task<EtlWorkerAcceptance> WaitForAcceptanceAsync(CancellationToken cancellationToken) =>
        WaitForJournalAsync<EtlWorkerAcceptance>(AcceptancePath, cancellationToken);

    public Task<EtlWorkerWrite> WaitForWriteAsync(CancellationToken cancellationToken) =>
        WaitForJournalAsync<EtlWorkerWrite>(EtlWorkerProtocol.WritePath(root, role), cancellationToken, AcceptancePath);

    private async Task<T> WaitForJournalAsync<T>(string path, CancellationToken cancellationToken,
        string? prematureAcceptancePath = null) where T : class
    {
        while (!File.Exists(path))
        {
            if (HasExited)
                throw new InvalidOperationException($"ETL worker {role} exited before its journal barrier. {Output}");
            if (prematureAcceptancePath is not null && File.Exists(prematureAcceptancePath))
                throw new InvalidOperationException("The actual worker reached acceptance before its required staged-write barrier.");
            await Task.Delay(25, cancellationToken);
        }
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), EtlWorkerProtocol.JsonOptions)
            ?? throw new InvalidDataException("The fixture worker journal is empty.");
    }

    public Task ReleaseAsync(CancellationToken cancellationToken = default) => File.WriteAllTextAsync(ReleasePath, "release", cancellationToken);

    public async Task CrashAsync()
    {
        if (!_started)
            return;
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (_process.HasExited) { }
            catch (System.ComponentModel.Win32Exception) when (_process.HasExited) { }
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _process.WaitForExitAsync(deadline.Token);
        await Task.WhenAll(_pumps).WaitAsync(deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await CrashAsync();
        _process.Dispose();
    }

    private string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    private async Task PumpAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (_output)
                {
                    _output.AppendLine(line);
                    if (_output.Length > 16_384)
                        _output.Remove(0, _output.Length - 16_384);
                    File.AppendAllText(Path.Combine(root, "journal", $"{role}.log"), line + Environment.NewLine);
                }
                if (line == $"{EtlWorkerProtocol.ReadyPrefix}{ProcessId}")
                    _ready.TrySetResult();
            }
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            throw; // Teardown observes the pump failure and retains fixture diagnostics.
        }
    }

    private static string HostAssemblyPath()
    {
        var configuration = typeof(EtlWorkerProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                continue;
            var path = Path.Combine(directory.FullName, "Extensions", "Etl", "tests", "WorkerHost", "bin", configuration, framework, "Elsa.Etl.WorkerHost.dll");
            return File.Exists(path) ? path : throw new FileNotFoundException("Build the ETL fixture host before running process tests.", path);
        }
        throw new InvalidOperationException("Could not locate the repository root for the ETL fixture host.");
    }
}
