namespace Elsa.Git;

/// <summary>
/// A single Git process-invocation stack shared across the foundation. Implementations shell out to the
/// <c>git</c> executable with <c>GIT_TERMINAL_PROMPT=0</c> so an unreachable or credential-protected remote
/// fails fast instead of blocking on an interactive prompt.
/// </summary>
public interface IGitClient
{
    /// <summary>
    /// Awaits a mutating Git command and throws <see cref="System.InvalidOperationException"/> on a non-zero exit.
    /// </summary>
    Task RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments);

    /// <summary>
    /// Runs a read-only Git command synchronously and returns its trimmed standard output, or an empty
    /// string on any failure.
    /// </summary>
    string RunOrDefault(string workingDirectory, params string[] arguments);

    /// <summary>
    /// Reports whether <paramref name="repositoryPath"/> is inside a Git work tree.
    /// </summary>
    bool IsGitRepository(string repositoryPath);
}
