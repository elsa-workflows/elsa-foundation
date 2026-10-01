namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The machine identity every export commit is made under (ADR 0034, commit shape). The workspace reads it back: a
/// Writer clone's unpushed commits under this identity are export output it may discard and regenerate, and a commit
/// under any other identity is never discarded (#2197). A commit counts as the export's only when its author name, its
/// author email and its committer email all are this identity, so a human's amend or rebase of an export commit, which
/// changes the committer, makes it theirs.
/// </summary>
internal static class GitExportIdentity
{
    public const string Name = "Elsa Design";

    public const string Email = "design@elsa.local";

    /// <summary>The <c>git log --format</c> that yields what <see cref="IsExport"/> reads, tab-separated.</summary>
    public const string LogFormat = "%an%x09%ae%x09%ce";

    /// <summary>The per-invocation <c>-c</c> arguments that make a commit under this identity.</summary>
    public static IReadOnlyList<string> CommitArgs { get; } = Array.AsReadOnly(["-c", $"user.name={Name}", "-c", $"user.email={Email}"]);

    public static bool IsExport(string authorName, string authorEmail, string committerEmail) =>
        string.Equals(authorName, Name, StringComparison.Ordinal)
        && string.Equals(authorEmail, Email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(committerEmail, Email, StringComparison.OrdinalIgnoreCase);
}
