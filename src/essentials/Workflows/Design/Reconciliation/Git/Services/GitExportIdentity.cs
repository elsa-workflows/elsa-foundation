namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The machine identity every export commit is made under (ADR 0034, commit shape). The workspace reads it back: a
/// Writer clone's unpushed commits under this identity are export output it may discard and regenerate, and a commit
/// under any other identity is never discarded (#2197).
/// </summary>
internal static class GitExportIdentity
{
    public const string Name = "Elsa Design";

    public const string Email = "design@elsa.local";

    /// <summary>The per-invocation <c>-c</c> arguments that make a commit under this identity.</summary>
    public static readonly string[] CommitArgs = ["-c", $"user.name={Name}", "-c", $"user.email={Email}"];
}
