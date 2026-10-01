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

    /// <summary>
    /// Local working-clone directory, used as given; give each process its own. Empty → a clone slot of this source under
    /// a per-user directory (<c>$XDG_RUNTIME_DIR</c> or the user's local application data, the OS temp dir only when neither is available),
    /// one per running process or shell and reused by the next one after a restart (see
    /// <c>Services.GitCloneSlot</c>, #2197).
    /// </summary>
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
    /// Fails fast on a <see cref="WorkflowsPath"/> that is not a plain relative path inside the clone. It reaches git as
    /// a pathspec of <c>clean -f -d</c> and <c>restore</c>, so an empty, rooted, <c>.</c> or <c>..</c> value would aim
    /// them at the whole clone or outside it. Git reads it literally, so a wildcard names only itself; a leading
    /// <c>:</c>, which git would otherwise read as pathspec magic such as <c>:/</c> or <c>:(top)</c>, is refused as well.
    /// </summary>
    public void ValidateWorkflowsPath()
    {
        if (string.IsNullOrWhiteSpace(WorkflowsPath)
            || Path.IsPathRooted(WorkflowsPath)
            || WorkflowsPath[0] is '/' or '\\' or ':'
            || WorkflowsPath.Split('/', '\\').Any(segment => segment is "." or ".."))
            throw new InvalidOperationException(
                $"{nameof(GitReconciliationOptions)}.{nameof(WorkflowsPath)} must be a relative path to a folder inside the repository, " +
                $"such as 'workflows': not empty, not rooted, not starting with ':', and with no '.' or '..' segment. Got '{WorkflowsPath}'.");
    }

    /// <summary>
    /// Fails fast on <see cref="Token"/> credentials git could not use as configured; nothing in other modes. The token
    /// reaches git as an environment variable that a credential helper writes into the credential protocol, one line per
    /// attribute, so a CR, LF or NUL in it would end the line, or the value, early and could add attributes of its own.
    /// A trailing newline, which a secret file or a pasted value commonly carries, is no part of the token and is
    /// trimmed by the feature before this check. Only HTTP(S) remotes authenticate by token: the helper is scoped to the
    /// remote's scheme and host, and an SSH or file remote has none to scope it to.
    /// </summary>
    public void ValidateTokenCredentials()
    {
        if (CredentialsMode != GitCredentialsMode.Token)
            return;

        if (Token.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
            throw new InvalidOperationException(
                $"{nameof(GitReconciliationOptions)}.{nameof(Token)} must not contain a carriage return, a line feed or a NUL character, " +
                "other than a trailing line break, which is trimmed.");

        if (HttpRemote(RemoteUrl) is null)
            throw new InvalidOperationException(
                $"{nameof(GitReconciliationOptions)}.{nameof(CredentialsMode)} {nameof(GitCredentialsMode.Token)} authenticates only to an http(s) remote, " +
                $"so '{RemoteUrl}' cannot be used with it. Use an https:// {nameof(RemoteUrl)}, or the {nameof(GitCredentialsMode.SshKey)} or {nameof(GitCredentialsMode.HostDefault)} mode.");
    }

    /// <summary><paramref name="remoteUrl"/> as a URI when it is an absolute <c>http</c> or <c>https</c> URL; otherwise <c>null</c>.</summary>
    public static Uri? HttpRemote(string remoteUrl) =>
        Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

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
