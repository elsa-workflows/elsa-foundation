using System.Diagnostics;
using System.Text.Json.Nodes;
using Elsa.Workbench.Tests;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

/// <summary>Runs the built Workbench with named target connections and supports a clean process restart.</summary>
public sealed class SharedPersistenceHostFixture : IAsyncDisposable
{
    public static IReadOnlyDictionary<string, string> PrimaryResourceSettings()
    {
        var settings = new Dictionary<string, string>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            // The original shared-layout fixture keeps its four-module scope. The separate
            // diagnostics fixture below enables both stores and removes their stock SQLite targets.
            ["CShells:Shells:default:Features:DiagnosticsOpenTelemetryEntityFrameworkCore"] = "false",
            ["CShells:Shells:default:Features:DiagnosticsStructuredLogsEntityFrameworkCore"] = "false"
        };
        return settings;
    }

    public static IReadOnlyDictionary<string, string> DiagnosticsResourceSettings()
    {
        var settings = PrimaryResourceSettings()
            .Where(entry => !entry.Key.EndsWith(":Features:DiagnosticsOpenTelemetryEntityFrameworkCore", StringComparison.Ordinal) &&
                            !entry.Key.EndsWith(":Features:DiagnosticsStructuredLogsEntityFrameworkCore", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        settings["Elsa:Persistence:Resources:diagnostics:Provider"] = "PostgreSql";
        settings["Elsa:Persistence:Resources:diagnostics:ConnectionName"] = "Diagnostics";
        settings["CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:DiagnosticsStructuredLogsEntityFrameworkCore"] = "diagnostics";
        settings["CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:DiagnosticsOpenTelemetryEntityFrameworkCore"] = "diagnostics";
        return settings;
    }

    public static void RemoveLegacyDiagnosticsTargets(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "shells.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var features = root["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["DiagnosticsStructuredLogsEntityFrameworkCore"]!.AsObject().Remove("ConnectionString");
        features["DiagnosticsOpenTelemetryEntityFrameworkCore"]!.AsObject().Remove("ConnectionString");
        File.WriteAllText(path, root.ToJsonString());
    }

    private readonly WorkbenchShell _shell;
    private readonly PostgreSqlTargetFixture _targets;
    private readonly Action<string>? _prepareContentRoot;
    private WorkbenchProcess? _process;
    private string? _stagedToolingHost;

    public SharedPersistenceHostFixture(
        PostgreSqlTargetFixture targets,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<string>? prepareContentRoot = null)
    {
        _targets = targets;
        _prepareContentRoot = prepareContentRoot;
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
    public async Task<ToolingRun> RunToolingAsync(
        string command, string? suppliedConnection = null, bool selectResource = true,
        string resource = "primary", bool stageAuthoredConfig = false)
    {
        if (command is not ("list" or "validate"))
            throw new ArgumentOutOfRangeException(nameof(command));
        var builtHostDirectory = Path.GetDirectoryName(WorkbenchBuild.AssemblyPath())!;
        var hostDirectory = stageAuthoredConfig ? StageToolingHost(builtHostDirectory) : builtHostDirectory;
        var targetFramework = Path.GetFileName(builtHostDirectory);
        var configuration = Path.GetFileName(Path.GetDirectoryName(builtHostDirectory)!);
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
            if (!File.ReadAllBytes(stageAuthoredConfig ? Path.Combine(Process.ContentRoot, target) : WorkbenchBuild.SourceFile(source))
                    .SequenceEqual(File.ReadAllBytes(Path.Combine(hostDirectory, target))))
                throw new InvalidOperationException($"The CLI host's {target} differs from the running Workbench source.");

        var arguments = new List<string>
        {
            cli, "persistence", command, "--host", hostDirectory,
            "--configuration-context", "workbench-json-environment-v1",
            "--environment", _shell.Environment, "--shell", "default",
            "--from-host"
        };
        if (selectResource)
            arguments.AddRange(["--resource", resource]);
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
        start.Environment["ELSA_EF_CONNECTION"] = suppliedConnection ??
            (resource == "diagnostics" ? _targets.DiagnosticsConnectionString : _targets.PrimaryConnectionString);

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

        _process = await WorkbenchProcess.StartAsync(_shell, _prepareContentRoot);
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
        RemoveStagedToolingHost();
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
            await _process.DisposeAsync();
        RemoveStagedToolingHost();
    }

    private string StageToolingHost(string builtHostDirectory)
    {
        if (_stagedToolingHost is not null)
            return _stagedToolingHost;

        var staged = Directory.CreateTempSubdirectory("elsa-resource-tooling-host-").FullName;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(builtHostDirectory))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("shells", StringComparison.OrdinalIgnoreCase) || name == ".nuplane" || name == "packages")
                    continue;
                CopyEntry(entry, Path.Combine(staged, name));
            }
            foreach (var file in Directory.EnumerateFiles(Process.ContentRoot, "*.json")
                         .Where(file => Path.GetFileName(file).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetFileName(file).StartsWith("shells", StringComparison.OrdinalIgnoreCase)))
                File.Copy(file, Path.Combine(staged, Path.GetFileName(file)));
            return _stagedToolingHost = staged;
        }
        catch
        {
            Directory.Delete(staged, recursive: true);
            throw;
        }
    }

    private void RemoveStagedToolingHost()
    {
        if (_stagedToolingHost is null)
            return;
        Directory.Delete(_stagedToolingHost, recursive: true);
        _stagedToolingHost = null;
    }

    private static void CopyEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
                CopyEntry(child, Path.Combine(destination, Path.GetFileName(child)));
        }
        else
            File.Copy(source, destination);
    }
}

public sealed record ToolingRun(int ExitCode, string Output);
