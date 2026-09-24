using System.Diagnostics;
using Elsa.Workbench.Tests;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

/// <summary>Runs the built Workbench with named target connections and supports a clean process restart.</summary>
public sealed class SharedPersistenceHostFixture : IAsyncDisposable
{
    public static IReadOnlyDictionary<string, string> PrimaryResourceSettings()
    {
        var settings = new Dictionary<string, string>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared"
        };
        foreach (var feature in new[]
                 {
                     "WorkflowsRuntimeEntityFrameworkCore",
                     "WorkflowsDesignEntityFrameworkCore",
                     "ActivitiesDesignEntityFrameworkCore",
                     "WorkflowsPublishingEntityFrameworkCore"
                 })
            settings[$"CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:{feature}"] = "primary";
        return settings;
    }

    private readonly WorkbenchShell _shell;
    private readonly PostgreSqlTargetFixture _targets;
    private WorkbenchProcess? _process;

    public SharedPersistenceHostFixture(
        PostgreSqlTargetFixture targets,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        _targets = targets;
        var combined = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Shared"] = targets.PrimaryConnectionString,
            ["ConnectionStrings:Diagnostics"] = targets.DiagnosticsConnectionString
        };

        if (settings is not null)
            foreach (var (key, value) in settings)
                combined[key] = value;

        _shell = WorkbenchShell.Development with { Settings = combined };
    }

    public WorkbenchProcess Process => _process ?? throw new InvalidOperationException("The Workbench has not started.");

    /// <summary>Runs the built CLI against the same authored files and inherited settings as this Workbench.</summary>
    public async Task<ToolingRun> RunToolingAsync(string command, string? suppliedConnection = null)
    {
        if (command is not ("list" or "validate"))
            throw new ArgumentOutOfRangeException(nameof(command));
        var hostDirectory = Path.GetDirectoryName(WorkbenchBuild.AssemblyPath())!;
        var targetFramework = Path.GetFileName(hostDirectory);
        var configuration = Path.GetFileName(Path.GetDirectoryName(hostDirectory)!);
        var cli = Path.Combine(WorkbenchBuild.RepositoryRoot, "src", "essentials", "Cli", "bin",
            configuration, targetFramework, "Elsa.Cli.dll");
        if (!File.Exists(cli))
            throw new FileNotFoundException("Build Elsa.Cli before running the shared persistence journey.", cli);

        var sources = new List<(string Source, string Target)>
        {
            ("appsettings.json", "appsettings.json"),
            (_shell.ShellFile, "shells.json")
        };
        var appsettingsOverlay = $"appsettings.{_shell.Environment}.json";
        if (File.Exists(WorkbenchBuild.SourceFile(appsettingsOverlay)))
            sources.Add((appsettingsOverlay, appsettingsOverlay));
        if (_shell.EnvironmentOverlay is { } shellOverlay)
            sources.Add((shellOverlay, $"shells.{_shell.Environment}.json"));
        foreach (var (source, target) in sources)
            if (!File.ReadAllBytes(WorkbenchBuild.SourceFile(source)).SequenceEqual(File.ReadAllBytes(Path.Combine(hostDirectory, target))))
                throw new InvalidOperationException($"The CLI host's {target} differs from the running Workbench source.");

        var arguments = new List<string>
        {
            cli, "persistence", command, "--host", hostDirectory,
            "--configuration-context", "workbench-json-environment-v1",
            "--environment", _shell.Environment, "--shell", "default", "--resource", "primary",
            "--from-host"
        };
        if (command == "validate")
            arguments.AddRange(["--provider", "PostgreSql", "--connection-env", "ELSA_EF_CONNECTION"]);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = WorkbenchBuild.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (key, value) in _shell.Settings)
            start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        start.Environment["ELSA_EF_CONNECTION"] = suppliedConnection ?? _targets.PrimaryConnectionString;

        using var process = new Process { StartInfo = start };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"The persistence {command} command did not exit within two minutes.");
        }
        return new ToolingRun(process.ExitCode, await output + await error);
    }

    public async Task StartAsync()
    {
        if (_process is not null)
            throw new InvalidOperationException("The Workbench is already running.");

        _process = await WorkbenchProcess.StartAsync(_shell);
    }

    public async Task RestartAsync()
    {
        if (_process is null)
            throw new InvalidOperationException("The Workbench has not started.");

        var stopped = await _process.StopAsync(TimeSpan.FromSeconds(30));
        if (stopped is null)
            throw new TimeoutException("The Workbench did not stop cleanly.");

        await _process.DisposeAsync();
        _process = null;
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
            await _process.DisposeAsync();
    }
}

public sealed record ToolingRun(int ExitCode, string Output);
