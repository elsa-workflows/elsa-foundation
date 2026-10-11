namespace Elsa.Git;

/// <summary>
/// Configures the <see cref="IGitClient"/> registered by
/// <see cref="GitClientServiceCollectionExtensions.AddGitClient"/>.
/// </summary>
public sealed class GitClientOptions
{
    /// <summary>
    /// The Git executable to invoke. Defaults to <c>"git"</c> (resolved from <c>PATH</c>). Set an absolute
    /// path when the host does not expose <c>git</c> on <c>PATH</c>.
    /// </summary>
    public string GitExecutable { get; set; } = "git";
}
