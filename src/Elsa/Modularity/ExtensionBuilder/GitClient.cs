using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Modularity.ExtensionBuilder;

/// <summary>
/// Canonical Git process invocation for the Extension Builder. This is the single Git stack:
/// <list type="bullet">
/// <item><see cref="RunAsync"/> awaits a mutating Git command and throws on a non-zero exit.</item>
/// <item><see cref="RunOrDefault"/> runs a read-only Git command synchronously and returns an empty
/// string on any failure.</item>
/// <item><see cref="IsGitRepository"/> reports whether a path is inside a Git work tree.</item>
/// </list>
/// Every invocation runs with <c>GIT_TERMINAL_PROMPT=0</c> so an unreachable or credential-protected
/// remote fails fast instead of blocking on an interactive prompt.
/// </summary>
internal sealed class GitClient(string gitExecutable, ILogger logger)
{
    public async Task RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        Process process;
        try
        {
            process = StartProcess(workingDirectory, arguments);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not start '{gitExecutable}'.", ex);
        }

        using (process)
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Git command failed: {string.Join(' ', arguments)}{Environment.NewLine}{error}".TrimEnd());
        }
    }

    public string RunOrDefault(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = StartProcess(workingDirectory, arguments);
            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : "";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Git operation failed, returning empty result.");
            return "";
        }
    }

    public bool IsGitRepository(string repositoryPath) =>
        string.Equals(RunOrDefault(repositoryPath, "rev-parse", "--is-inside-work-tree"), "true", StringComparison.OrdinalIgnoreCase);

    private Process StartProcess(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = CreateStartInfo(gitExecutable, workingDirectory, arguments);
        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{gitExecutable}'.");
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for a git invocation. Notably sets
    /// <c>GIT_TERMINAL_PROMPT=0</c> so an unreachable or credential-protected remote fails fast
    /// instead of blocking on an interactive prompt. Exposed to tests so this env var can be asserted
    /// directly, independent of whether the host has a TTY.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(string gitExecutable, string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup after cancellation.
        }
    }
}
