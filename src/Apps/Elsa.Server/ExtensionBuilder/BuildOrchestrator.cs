using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Elsa.Server.ExtensionBuilder;

/// <summary>
/// Runs a resolved repository build or pack by shelling out to the configured <c>dotnet</c>
/// executable, capturing the log, parsing diagnostics, and reading the produced pack artifacts. It
/// produces the terminal <see cref="BuildResult"/> (Succeeded/Failed, including the cancellation and
/// no-artifacts cases); the storage façade owns the surrounding state transitions and persistence.
/// </summary>
internal sealed partial class BuildOrchestrator(string dotNetExecutable)
{
    public async Task<BuildResult> RunAsync(
        string repositoryPath,
        BuildResult running,
        RepositoryBuildCommand commandName,
        string targetPath,
        string? artifactsPath,
        string logPath,
        ExtensionProject project,
        string workspaceId,
        string sourceRevisionId,
        string? sourceBranch,
        bool sourceIsDirty,
        CancellationToken cancellationToken)
    {
        var command = commandName.ToString().ToLowerInvariant();
        var diagnostics = new List<BuildDiagnostic>();
        var arguments = artifactsPath is null
            ? [command, targetPath]
            : new[] { command, targetPath, "--output", artifactsPath };

        ProcessResult process;
        try
        {
            process = await RunProcessAsync(dotNetExecutable, repositoryPath, cancellationToken, arguments);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, $"{commandName} was canceled.", null, null, null, null));
            await File.WriteAllLinesAsync(logPath, diagnostics.Select(diagnostic => diagnostic.Message), CancellationToken.None);
            return running with { Status = BuildStatus.Failed, Diagnostics = diagnostics, CompletedAt = DateTimeOffset.UtcNow };
        }

        var log = new[] { process.Output, process.Error }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(text => text.Split(Environment.NewLine))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        await File.WriteAllLinesAsync(logPath, log, cancellationToken);
        diagnostics.AddRange(ExtensionBuilderBuildRunner.ParseDiagnostics(log));
        IReadOnlyList<BuildArtifact> artifacts = artifactsPath is null
            ? []
            : ReadPackArtifacts(running.Id, artifactsPath, project, workspaceId, sourceRevisionId, sourceBranch, sourceIsDirty);
        if (process.ExitCode != 0 && diagnostics.All(diagnostic => diagnostic.Severity is not BuildDiagnosticSeverity.Error))
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, $"{commandName} failed.", null, null, null, null));
        if (commandName is RepositoryBuildCommand.Pack && process.ExitCode == 0 && artifacts.Count == 0)
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, "Pack completed without producing package artifacts.", null, null, null, null));

        return running with
        {
            Status = process.ExitCode == 0 && diagnostics.All(diagnostic => diagnostic.Severity is not BuildDiagnosticSeverity.Error)
                ? BuildStatus.Succeeded
                : BuildStatus.Failed,
            Diagnostics = diagnostics,
            Artifact = artifacts.FirstOrDefault(),
            Artifacts = artifacts,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<BuildArtifact> ReadPackArtifacts(string buildId, string artifactsPath, ExtensionProject project, string workspaceId, string sourceRevisionId, string? sourceBranch, bool sourceIsDirty) =>
        Directory.EnumerateFiles(artifactsPath, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => ReadPackArtifact(buildId, path, project, workspaceId, sourceRevisionId, sourceBranch, sourceIsDirty))
            .ToArray();

    private static BuildArtifact ReadPackArtifact(string buildId, string artifactPath, ExtensionProject project, string workspaceId, string sourceRevisionId, string? sourceBranch, bool sourceIsDirty)
    {
        var (packageId, version) = TryReadPackageIdentity(artifactPath) ?? (project.PackageId, project.PackageVersion);
        var file = new FileInfo(artifactPath);
        return new($"artifact_{Guid.NewGuid():N}", buildId, packageId, version, file.Name, file.FullName, file.Length, DateTimeOffset.UtcNow, workspaceId, sourceRevisionId, sourceBranch, sourceIsDirty);
    }

    private static (string PackageId, string Version)? TryReadPackageIdentity(string artifactPath)
    {
        try
        {
            return ExtensionBuilderBuildRunner.TryReadNuspecIdentity(artifactPath);
        }
        catch (InvalidDataException)
        {
        }

        var match = PackageIdentityRegex().Match(Path.GetFileNameWithoutExtension(artifactPath));
        return match.Success ? (match.Groups["id"].Value, match.Groups["version"].Value) : null;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            using var process = StartProcess(fileName, workingDirectory, arguments);
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                return new(process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Could not start '{fileName}'.", ex);
        }
    }

    private static Process StartProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
    }

    [GeneratedRegex(@"^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$")]
    private static partial Regex PackageIdentityRegex();

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
