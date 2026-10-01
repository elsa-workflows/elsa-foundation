using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Git;

/// <summary>
/// Canonical Git process invocation for the foundation. This is the single Git stack:
/// <list type="bullet">
/// <item><see cref="RunAsync(string, CancellationToken, string[])"/> awaits a Git command, returns its trimmed standard
/// output, and throws on a non-zero exit; an overload adds environment variables to that one process.</item>
/// <item><see cref="RunOrDefault"/> runs a read-only Git command synchronously and returns an empty
/// string on any failure.</item>
/// <item><see cref="IsGitRepository"/> reports whether a path is inside a Git work tree.</item>
/// </list>
/// Every invocation runs with <c>GIT_TERMINAL_PROMPT=0</c> so an unreachable or credential-protected
/// remote fails fast instead of blocking on an interactive prompt.
/// </summary>
public sealed class GitClient(string gitExecutable, ILogger logger) : IGitClient
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    public Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments) =>
        RunAsync(workingDirectory, NoEnvironment, cancellationToken, arguments);

    public async Task<string> RunAsync(
        string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken, params string[] arguments)
    {
        Process process;
        try
        {
            process = StartProcess(workingDirectory, arguments, environment);
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

            return output.Trim();
        }
    }

    public string RunOrDefault(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = StartProcess(workingDirectory, arguments, NoEnvironment);
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

    private Process StartProcess(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = CreateStartInfo(gitExecutable, workingDirectory, arguments, environment);
        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{gitExecutable}'.");
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for a git invocation, with <paramref name="environment"/> added to the
    /// process environment. Notably sets <c>GIT_TERMINAL_PROMPT=0</c>, after that environment so it cannot be undone,
    /// so an unreachable or credential-protected remote fails fast instead of blocking on an interactive prompt.
    /// Public (per §2.23.3, logic-bearing implementations are directly testable without <c>InternalsVisibleTo</c>) so
    /// tests can assert this env var directly, independent of whether the host has a TTY.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(
        string gitExecutable, string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var (name, value) in environment ?? NoEnvironment)
            startInfo.Environment[name] = value;
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
