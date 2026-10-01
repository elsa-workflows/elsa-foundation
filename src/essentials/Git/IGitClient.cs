namespace Elsa.Git;

/// <summary>
/// A single Git process-invocation stack shared across the foundation. Implementations shell out to the
/// <c>git</c> executable with <c>GIT_TERMINAL_PROMPT=0</c> so an unreachable or credential-protected remote
/// fails fast instead of blocking on an interactive prompt.
/// </summary>
public interface IGitClient
{
    /// <summary>
    /// Awaits a Git command and returns its trimmed standard output. Throws <see cref="System.InvalidOperationException"/>
    /// on a non-zero exit, so a caller that decides from the output never mistakes a failed command for an empty answer.
    /// </summary>
    Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments);

    /// <summary>
    /// Awaits a Git command as <see cref="RunAsync(string, CancellationToken, string[])"/> does, with
    /// <paramref name="environment"/> added to the environment of that one Git process. For a value that must stay off the
    /// command line, such as a token a credential helper reads: unlike an argument, the environment of a process is
    /// readable only by its own user.
    /// </summary>
    Task<string> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken, params string[] arguments);

    /// <summary>
    /// Runs a read-only Git command synchronously and returns its trimmed standard output, or an empty
    /// string on any failure.
    /// </summary>
    string RunOrDefault(string workingDirectory, params string[] arguments);

    /// <summary>
    /// Reports whether <paramref name="repositoryPath"/> is the top level of a Git work tree. A directory nested in the
    /// work tree of another repository is not: it holds no repository of its own.
    /// </summary>
    bool IsGitRepository(string repositoryPath);
}
