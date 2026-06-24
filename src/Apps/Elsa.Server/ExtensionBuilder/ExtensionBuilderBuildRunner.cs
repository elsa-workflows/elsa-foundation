using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Server.ExtensionBuilder;

internal interface IExtensionBuilderBuildRunner
{
    Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default);
}

internal interface IExtensionBuilderBuildQueue
{
    Task EnqueueAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default);
}

internal interface IExtensionBuilderBuildExecutor
{
    Task ExecuteAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default);
}

internal sealed record ExtensionBuilderBuildWorkItem(
    ExtensionProject Project,
    SourceSnapshot Snapshot,
    string BuildId,
    string LogPath,
    string ArtifactsPath);

internal sealed class ExtensionBuilderBackgroundBuildQueue : IExtensionBuilderBuildQueue
{
    private readonly Channel<ExtensionBuilderBuildWorkItem> _channel = Channel.CreateUnbounded<ExtensionBuilderBuildWorkItem>(new()
    {
        SingleReader = true
    });

    public Task EnqueueAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(workItem, cancellationToken).AsTask();

    public IAsyncEnumerable<ExtensionBuilderBuildWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class ExtensionBuilderBuildWorker(
    ExtensionBuilderBackgroundBuildQueue queue,
    IExtensionBuilderStorage storage,
    IServiceScopeFactory scopeFactory,
    ILogger<ExtensionBuilderBuildWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recoveredBuilds = await storage.FailIncompleteBuildsAsync(stoppingToken);
        if (recoveredBuilds > 0)
            logger.LogWarning("Marked {BuildCount} stale Extension Builder builds as failed after server startup.", recoveredBuilds);

        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IExtensionBuilderBuildExecutor>();
            try
            {
                await executor.ExecuteAsync(workItem, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Extension Builder build {BuildId} failed outside the normal build result path.", workItem.BuildId);
            }
        }
    }
}

internal sealed class ExtensionBuilderBuildExecutor(
    IExtensionBuilderStorage storage,
    IExtensionBuilderBuildRunner buildRunner,
    ILogger<ExtensionBuilderBuildExecutor> logger) : IExtensionBuilderBuildExecutor
{
    public async Task ExecuteAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default)
    {
        if (!await storage.ProjectExistsAsync(workItem.Project.Id, cancellationToken))
            return;

        BuildResult completed;
        var running = new BuildResult(
            workItem.BuildId,
            workItem.Project.Id,
            workItem.Project.WorkspaceId,
            workItem.Snapshot.Id,
            BuildStatus.Running,
            [],
            null,
            workItem.LogPath,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        try
        {
            completed = await buildRunner.RunAsync(workItem.Project, workItem.Snapshot, workItem.BuildId, workItem.LogPath, workItem.ArtifactsPath, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            completed = await CreateFailedBuildAsync(running, "Build was canceled before completion.", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            completed = await CreateFailedBuildAsync(running, ex.Message, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Extension Builder build {BuildId} failed unexpectedly.", workItem.BuildId);
            completed = await CreateFailedBuildAsync(running, "Build failed unexpectedly. See the build log for details.", ex);
        }

        if (!await storage.ProjectExistsAsync(workItem.Project.Id, CancellationToken.None))
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(workItem.LogPath)!);
            return;
        }

        if (!await storage.SaveBuildAsync(completed, CancellationToken.None))
            DeleteDirectoryIfExists(Path.GetDirectoryName(workItem.LogPath)!);
    }

    private static async Task<BuildResult> CreateFailedBuildAsync(BuildResult running, string message, Exception exception)
    {
        var diagnostic = new BuildDiagnostic(BuildDiagnosticSeverity.Error, message, null, null, null, null);
        await File.WriteAllTextAsync(running.LogPath, exception.ToString(), CancellationToken.None);
        return running with
        {
            Status = BuildStatus.Failed,
            Diagnostics = [diagnostic],
            Artifact = null,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

internal sealed partial class ExtensionBuilderBuildRunner(IOptions<ExtensionBuilderOptions> options) : IExtensionBuilderBuildRunner
{
    private static readonly Dictionary<string, SemaphoreSlim> BuildLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object BuildLocksGate = new();

    public async Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default)
    {
        var buildLock = GetBuildLock(project.Id);
        await buildLock.WaitAsync(cancellationToken);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(artifactsPath);
            var log = new List<string>();
            var logGate = new object();
            var diagnostics = new List<BuildDiagnostic>();

            var projectFile = Directory.EnumerateFiles(snapshot.Path, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (projectFile is null)
            {
                diagnostics.Add(new(BuildDiagnosticSeverity.Error, "Project file was not found in the source snapshot.", null, null, null, null));
                await File.WriteAllLinesAsync(logPath, diagnostics.Select(x => x.Message), cancellationToken);
                return Failed(project, snapshot, buildId, logPath, diagnostics, startedAt);
            }

            var dotnet = ResolveDotNetExecutable(options.Value.DotNetExecutable);
            var restoreExit = await RunProcessAsync(dotnet, $"restore \"{projectFile}\"", snapshot.Path, log, logGate, cancellationToken);
            var packExit = restoreExit == 0
                ? await RunProcessAsync(dotnet, $"pack \"{projectFile}\" --no-restore -c Release -o \"{artifactsPath}\"", snapshot.Path, log, logGate, cancellationToken)
                : -1;

            await File.WriteAllLinesAsync(logPath, log, cancellationToken);
            diagnostics.AddRange(ParseDiagnostics(log));

            if (restoreExit != 0 && diagnostics.All(x => x.Severity is not BuildDiagnosticSeverity.Error))
                diagnostics.Add(new(BuildDiagnosticSeverity.Error, "Dependency restore failed.", null, null, null, null));

            if (packExit != 0 && diagnostics.All(x => x.Severity is not BuildDiagnosticSeverity.Error))
                diagnostics.Add(new(BuildDiagnosticSeverity.Error, "Package build failed.", null, null, null, null));

            var artifactPath = Directory.EnumerateFiles(artifactsPath, "*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (restoreExit != 0 || packExit != 0 || artifactPath is null)
                return Failed(project, snapshot, buildId, logPath, diagnostics, startedAt);

            var artifact = ReadArtifact(buildId, artifactPath, project.PackageId, project.PackageVersion);
            return new(
                buildId,
                project.Id,
                project.WorkspaceId,
                snapshot.Id,
                BuildStatus.Succeeded,
                diagnostics,
                artifact,
                logPath,
                startedAt,
                startedAt,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            buildLock.Release();
        }
    }

    internal static IReadOnlyList<BuildDiagnostic> ParseDiagnostics(IEnumerable<string> lines) =>
        lines.Select(ParseDiagnostic).Where(x => x is not null).Cast<BuildDiagnostic>().ToArray();

    private static BuildDiagnostic? ParseDiagnostic(string line)
    {
        var match = DiagnosticRegex().Match(line);
        if (!match.Success)
            return null;

        var severity = match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? BuildDiagnosticSeverity.Error
            : BuildDiagnosticSeverity.Warning;
        return new(
            severity,
            match.Groups["message"].Value.Trim(),
            EmptyToNull(match.Groups["file"].Value),
            TryInt(match.Groups["line"].Value),
            TryInt(match.Groups["column"].Value),
            EmptyToNull(match.Groups["code"].Value));
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory, List<string> log, object logGate, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (logGate)
                    log.Add(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (logGate)
                    log.Add(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw;
        }
        return process.ExitCode;
    }

    private static BuildResult Failed(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, IReadOnlyList<BuildDiagnostic> diagnostics, DateTimeOffset startedAt) =>
        new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Failed, diagnostics, null, logPath, startedAt, startedAt, DateTimeOffset.UtcNow);

    private static BuildArtifact ReadArtifact(string buildId, string artifactPath, string fallbackPackageId, string fallbackVersion)
    {
        var (packageId, version) = TryReadNuspecIdentity(artifactPath) ?? (fallbackPackageId, fallbackVersion);
        var file = new FileInfo(artifactPath);
        return new($"artifact_{Guid.NewGuid():N}", buildId, packageId, version, file.Name, file.FullName, file.Length, DateTimeOffset.UtcNow);
    }

    internal static (string PackageId, string Version)? TryReadNuspecIdentity(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var nuspec = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
            return null;

        using var nuspecStream = nuspec.Open();
        var document = XDocument.Load(nuspecStream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(ns + "metadata");
        var id = metadata?.Element(ns + "id")?.Value;
        var version = metadata?.Element(ns + "version")?.Value;
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) ? null : (id, version);
    }

    private static SemaphoreSlim GetBuildLock(string projectId)
    {
        lock (BuildLocksGate)
        {
            if (!BuildLocks.TryGetValue(projectId, out var buildLock))
            {
                buildLock = new(1, 1);
                BuildLocks[projectId] = buildLock;
            }

            return buildLock;
        }
    }

    private static string ResolveDotNetExecutable(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var candidate = Path.Combine(dotnetRoot, "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        const string macOSDefault = "/usr/local/share/dotnet/dotnet";
        return File.Exists(macOSDefault) ? macOSDefault : "dotnet";
    }

    private static int? TryInt(string value) => int.TryParse(value, out var result) ? result : null;
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"^(?:(?<file>.*)\((?<line>\d+),(?<column>\d+)\):\s*)?(?<severity>warning|error)\s+(?<code>[A-Z]+\d+):\s*(?<message>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticRegex();
}
