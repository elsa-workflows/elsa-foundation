namespace Elsa.Workflows.Design.Reconciliation.Git.Options;

/// <summary>
/// Bound configuration for the git reconciliation source + export sink (ADR 0034 config shape). The
/// concrete <c>WorkflowsDesignGitReconciliationFeature</c> surfaces these as CShells settings and
/// registers this instance so the source, workspace, and exporter share one resolved configuration.
/// </summary>
public sealed class GitReconciliationOptions
{
    /// <summary>The remote to clone/fetch, e.g. <c>git@github.com:acme/workflows.git</c>.</summary>
    public string RemoteUrl { get; set; } = string.Empty;

    /// <summary>The tracked branch. Default <c>main</c>.</summary>
    public string Branch { get; set; } = "main";

    /// <summary>Repo-relative root under which definitions live. Default <c>workflows</c>.</summary>
    public string WorkflowsPath { get; set; } = "workflows";

    /// <summary>Local working-clone directory. Empty → a stable path under the host data dir.</summary>
    public string LocalCachePath { get; set; } = string.Empty;

    /// <summary>The role (Writer|Consumer) — drives clone mode and export (D11).</summary>
    public GitReconciliationRole Role { get; set; } = GitReconciliationRole.Consumer;

    /// <summary>How credentials are supplied (FR-013).</summary>
    public GitCredentialsMode CredentialsMode { get; set; } = GitCredentialsMode.HostDefault;

    /// <summary>SSH private-key path (SshKey mode). A path, not a secret.</summary>
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>HTTPS token (Token mode). Bound as a secret by the feature.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Export settings (honored only when Role=Writer).</summary>
    public GitExportOptions Export { get; set; } = new();

    /// <summary>
    /// Stable source identity recorded per contributed entry: the remote + branch, so two branches/repos
    /// are distinct sources.
    /// </summary>
    public string ResolvedSourceId => $"{RemoteUrl}#{Branch}";

    /// <summary>
    /// The effective local clone path. Explicit <see cref="LocalCachePath"/> when set, else a stable
    /// per-source directory under the OS temp/data area keyed by a hash of remote+branch so distinct
    /// sources never collide.
    /// </summary>
    public string ResolveLocalCachePath()
    {
        if (!string.IsNullOrWhiteSpace(LocalCachePath))
            return Path.GetFullPath(LocalCachePath);

        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(ResolvedSourceId)))[..16].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "elsa-gitops", key);
    }

    /// <summary>The branch export pushes to: explicit <see cref="GitExportOptions.Branch"/> else <see cref="Branch"/>.</summary>
    public string ResolvedExportBranch => string.IsNullOrWhiteSpace(Export.Branch) ? Branch : Export.Branch;
}

/// <summary>Export sub-options (FR-009/FR-010/FR-011).</summary>
public sealed class GitExportOptions
{
    /// <summary>When local export commits reach the remote.</summary>
    public GitPushMode PushMode { get; set; } = GitPushMode.Manual;

    /// <summary>Export branch. Empty → the tracked <see cref="GitReconciliationOptions.Branch"/>.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Emit a <c>wf/{definitionId}/v{version}</c> tag per exported version. Default true.</summary>
    public bool Tag { get; set; } = true;
}
