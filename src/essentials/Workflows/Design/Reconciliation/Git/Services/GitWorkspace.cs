using System.Globalization;
using Elsa.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Default <see cref="IGitWorkspace"/>: manages a single local working clone with role-based clone modes
/// (ADR 0034 D11). Credentials are applied as per-invocation <c>-c …</c> arguments (never on a visible
/// URL/argv secret, FR-013). A Writer clone is a persistent working copy that keeps the export commits it has yet to
/// push for as long as the remote has not moved, and is otherwise brought to the remote without ever discarding a commit
/// the export did not make (#2197); a Consumer clone is a disposable mirror.
/// </summary>
public sealed class GitWorkspace(
    IGitClient gitClient,
    IOptions<GitReconciliationOptions> options,
    ILogger<GitWorkspace> logger) : IGitWorkspace
{
    private readonly GitReconciliationOptions _options = options.Value;
    private string[] _credentialArgs = [];

    public string RepositoryPath => _options.ResolveLocalCachePath();

    public IReadOnlyList<string> CredentialArgs => _credentialArgs;

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var repoPath = _options.ResolveLocalCachePath();
        _credentialArgs = PrepareCredentialArgs(repoPath);

        if (!gitClient.IsGitRepository(repoPath))
            await CloneAsync(repoPath, cancellationToken);
        else
            await IntegrateAsync(repoPath, cancellationToken);

        return repoPath;
    }

    private async Task CloneAsync(string repoPath, CancellationToken cancellationToken)
    {
        // git clone requires an empty/absent target. A pre-existing non-repo directory carries no
        // commits, so clearing it is safe for both roles.
        if (Directory.Exists(repoPath))
            Directory.Delete(repoPath, recursive: true);

        var parent = Directory.GetParent(repoPath)?.FullName ?? Path.GetTempPath();
        Directory.CreateDirectory(parent);

        LogClone(_options.RemoteUrl, _options.Branch);
        await gitClient.RunAsync(parent, cancellationToken,
            [.. _credentialArgs, "clone", "--branch", _options.Branch, "--single-branch", _options.RemoteUrl, repoPath]);
    }

    private async Task IntegrateAsync(string repoPath, CancellationToken cancellationToken)
    {
        await gitClient.RunAsync(repoPath, cancellationToken, GitRemoteRefs.FetchArgs(_credentialArgs, _options.Branch));
        var remote = GitRemoteRefs.Tracking(_options.Branch);

        if (_options.Role != GitReconciliationRole.Writer)
        {
            // Consumer: disposable mirror.
            await gitClient.RunAsync(repoPath, cancellationToken, "reset", "--hard", remote);
            return;
        }

        await DiscardExportResidueAsync(repoPath, cancellationToken);
        if (await WriterTargetAsync(repoPath, remote, cancellationToken) is { } target)
        {
            // --keep moves the branch as --hard would, but keeps uncommitted changes anywhere else in the clone, which are
            // not the export's, and fails rather than overwrite one.
            await gitClient.RunAsync(repoPath, cancellationToken, "reset", "--keep", "-q", target);
        }
    }

    /// <summary>
    /// Uncommitted changes under the workflows path are residue of an export that stopped between writing a file and
    /// committing it. They are discarded, so the import and the export read only what is committed, and the export writes
    /// the file again from the catalog. Nothing outside the workflows path is touched.
    /// </summary>
    private async Task DiscardExportResidueAsync(string repoPath, CancellationToken cancellationToken)
    {
        var changed = (await gitClient.RunAsync(repoPath, cancellationToken, "diff", "--name-only", "-z", "HEAD", "--", _options.WorkflowsPath))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (changed.Length > 0)
            await gitClient.RunAsync(repoPath, cancellationToken, ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. changed]);

        await gitClient.RunAsync(repoPath, cancellationToken, "clean", "-f", "-d", "-q", "--", _options.WorkflowsPath);
    }

    /// <summary>
    /// Where a Writer clone moves, decided from how its branch stands against the remote (#2197); <c>null</c> when it stays.
    /// Up to date or only ahead, it stays, keeping the export commits it has yet to push. Only behind, it fast-forwards.
    /// Diverged, because another writer pushed: when every commit the remote lacks was made by the export, those commits
    /// are output the export regenerates from the catalog, so the clone moves to the remote; when any was not, it stays
    /// and the divergence is logged as an error. Neither way throws, so a diverged clone never fails a shell start.
    /// </summary>
    private async Task<string?> WriterTargetAsync(string repoPath, string remote, CancellationToken cancellationToken)
    {
        var counts = (await gitClient.RunAsync(repoPath, cancellationToken, "rev-list", "--left-right", "--count", $"HEAD...{remote}"))
            .Split('\t', StringSplitOptions.TrimEntries);
        var ahead = int.Parse(counts[0], CultureInfo.InvariantCulture);
        var behind = int.Parse(counts[1], CultureInfo.InvariantCulture);

        if (behind == 0)
            return null;
        if (ahead == 0)
            return remote;

        var foreignCommits = await ForeignCommitsAsync(repoPath, remote, cancellationToken);
        if (foreignCommits.Count == 0)
        {
            LogDiscardingExportCommits(repoPath, ahead, behind);
            return remote;
        }

        LogKeepingDivergedClone(repoPath, ahead, behind, foreignCommits);
        return null;
    }

    /// <summary>The commits this clone has and the remote lacks that the export did not make, as <c>{hash} {email}</c>.</summary>
    private async Task<IReadOnlyList<string>> ForeignCommitsAsync(string repoPath, string remote, CancellationToken cancellationToken)
    {
        var log = await gitClient.RunAsync(repoPath, cancellationToken, "log", "--format=%h %ae", $"{remote}..HEAD");
        return log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.EndsWith($" {GitExportIdentity.Email}", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Builds the credential <c>-c …</c> prefix per <see cref="GitCredentialsMode"/>, writing a 0600
    /// credential-store file for Token mode so the token never appears on argv. HostDefault contributes
    /// nothing (ambient helper / ssh-agent).
    /// </summary>
    private string[] PrepareCredentialArgs(string repoPath)
    {
        switch (_options.CredentialsMode)
        {
            case GitCredentialsMode.SshKey when !string.IsNullOrWhiteSpace(_options.KeyPath):
                return ["-c", $"core.sshCommand=ssh -i {_options.KeyPath} -o IdentitiesOnly=yes"];

            case GitCredentialsMode.Token when !string.IsNullOrWhiteSpace(_options.Token):
                var credFile = WriteTokenCredentialFile(repoPath);
                return ["-c", $"credential.helper=store --file={credFile}"];

            default:
                return [];
        }
    }

    private string WriteTokenCredentialFile(string repoPath)
    {
        var dir = Directory.GetParent(repoPath)?.FullName ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        var credFile = Path.Combine(dir, ".git-credentials");
        var host = TryGetHost(_options.RemoteUrl);
        File.WriteAllText(credFile, $"https://x-access-token:{_options.Token}@{host}{Environment.NewLine}");
        TrySetOwnerOnlyPermissions(credFile);
        return credFile;
    }

    private static string TryGetHost(string remoteUrl) =>
        Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) ? uri.Host : "github.com";

    private static void TrySetOwnerOnlyPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort hardening; the credential helper still functions without it.
        }
    }

    private void LogClone(string remoteUrl, string branch)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Cloning workflows repository {url} (branch {branch})", remoteUrl, branch);
    }

    private void LogDiscardingExportCommits(string repoPath, int ahead, int behind)
    {
        logger.LogWarning(
            "The workflows clone at '{path}' has diverged from origin/{branch}: another writer pushed {behind} commit(s) it lacks. " +
            "Its {ahead} unpushed export commit(s) are discarded and the clone moves to the remote; the export writes again from the catalog whatever the remote lacks.",
            repoPath, _options.Branch, behind, ahead);
    }

    private void LogKeepingDivergedClone(string repoPath, int ahead, int behind, IReadOnlyList<string> foreignCommits)
    {
        logger.LogError(
            "The workflows clone at '{path}' has diverged from origin/{branch} ({ahead} commit(s) of its own, {behind} of the remote's), and commits not made by the export are among its own: {commits}. " +
            "It was left as it is, so nothing is lost: the import reads it as it stands and the export's push is refused until the clone is reconciled with the remote by hand.",
            repoPath, _options.Branch, ahead, behind, string.Join(", ", foreignCommits));
    }
}
