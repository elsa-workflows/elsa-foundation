using System.Collections.Concurrent;
using Elsa.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Default <see cref="IGitWorkspace"/>: manages the shell's local working clone, which lives where its
/// <see cref="GitCloneSlot"/> says, with role-based clone modes (ADR 0034 D11). Commands that reach the remote carry the
/// <see cref="GitCredentials"/>, so nothing secret is on the command line or on disk (FR-013). A Writer clone is a
/// persistent working copy that keeps the export commits it has yet to push for as long as the remote has not moved, and
/// is otherwise brought to the remote without ever discarding a commit the export did not make (#2197); a Consumer clone
/// is a disposable mirror.
/// </summary>
public sealed class GitWorkspace(
    IGitClient gitClient,
    IOptions<GitReconciliationOptions> options,
    GitCloneSlot cloneSlot,
    ILogger<GitWorkspace> logger) : IGitWorkspace
{
    private const string Diverged = "diverged";
    private const string MoveRefused = "move";

    // Every pass of every shell in the process runs EnsureReadyAsync, and a clone that stays diverged is diverged on each
    // one, so an error about it is reported once per clone until the clone is found healthy again.
    private static readonly ConcurrentDictionary<string, byte> ReportedClones = new(StringComparer.Ordinal);

    private readonly GitReconciliationOptions _options = options.Value;
    private readonly GitCredentials _credentials = GitCredentials.For(options.Value);

    public string RepositoryPath => cloneSlot.RepositoryPath;

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var repoPath = cloneSlot.RepositoryPath;

        if (!gitClient.IsGitRepository(repoPath))
            await CloneAsync(repoPath, cancellationToken);
        else
            await IntegrateAsync(repoPath, cancellationToken);

        return repoPath;
    }

    public Task<string> RunRemoteAsync(CancellationToken cancellationToken, params string[] arguments) =>
        RunRemoteAsync(cloneSlot.RepositoryPath, cancellationToken, arguments);

    private Task<string> RunRemoteAsync(string workingDirectory, CancellationToken cancellationToken, string[] arguments) =>
        gitClient.RunAsync(workingDirectory, _credentials.Environment, cancellationToken, [.. _credentials.Arguments, .. arguments]);

    private async Task CloneAsync(string repoPath, CancellationToken cancellationToken)
    {
        // git clone requires an empty/absent target. A pre-existing non-repo directory carries no
        // commits, so clearing it is safe for both roles.
        if (Directory.Exists(repoPath))
            Directory.Delete(repoPath, recursive: true);

        var parent = Directory.GetParent(repoPath)?.FullName ?? Path.GetTempPath();
        Directory.CreateDirectory(parent);

        LogClone(_options.RemoteUrl, _options.Branch);
        await RunRemoteAsync(parent, cancellationToken, ["clone", "--branch", _options.Branch, "--single-branch", _options.RemoteUrl, repoPath]);
    }

    private async Task IntegrateAsync(string repoPath, CancellationToken cancellationToken)
    {
        await RunRemoteAsync(repoPath, cancellationToken, GitRemoteRefs.FetchArgs(_options.Branch));
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
            await MoveToAsync(repoPath, target, cancellationToken);
        }
    }

    /// <summary>
    /// <c>reset --keep</c> moves the branch as <c>--hard</c> would, but keeps uncommitted changes anywhere else in the
    /// clone, which are not the export's. It fails rather than overwrite one; the clone then stays as it stands, with an
    /// error in the log, and does not fail the start, as with a commit the export did not make.
    /// </summary>
    private async Task MoveToAsync(string repoPath, string target, CancellationToken cancellationToken)
    {
        try
        {
            await gitClient.RunAsync(repoPath, cancellationToken, "reset", "--keep", "-q", target);
        }
        catch (InvalidOperationException exception)
        {
            if (FirstReport(repoPath, MoveRefused))
                LogMoveRefused(repoPath, target, exception);
        }
    }

    /// <summary>
    /// Uncommitted changes under the workflows path are residue of an export that stopped between writing a file and
    /// committing it. They are discarded, so the import and the export read only what is committed, and the export writes
    /// the file again from the catalog. Nothing outside the workflows path is touched: every path is read literally, so
    /// pathspec magic or a wildcard in it names only itself (see <see cref="GitPathspecs"/>).
    /// </summary>
    private async Task DiscardExportResidueAsync(string repoPath, CancellationToken cancellationToken)
    {
        var changed = (await gitClient.RunAsync(repoPath, cancellationToken,
                GitPathspecs.Literal, "diff", "--name-only", "-z", "HEAD", "--", _options.WorkflowsPath))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (changed.Length > 0)
            await gitClient.RunAsync(repoPath, cancellationToken, [GitPathspecs.Literal, "restore", "--source=HEAD", "--staged", "--worktree", "--", .. changed]);

        await gitClient.RunAsync(repoPath, cancellationToken, GitPathspecs.Literal, "clean", "-f", "-d", "-q", "--", _options.WorkflowsPath);
    }

    /// <summary>
    /// Where a Writer clone moves, decided from how its branch stands against the remote (#2197); <c>null</c> when it stays.
    /// Up to date or only ahead, it stays, keeping the export commits it has yet to push, and is healthy: what was reported
    /// about it is forgotten, so a problem that comes back after a repair is reported again. Only behind, it fast-forwards.
    /// Diverged, because another writer pushed: when every commit the remote lacks was made by the export, those commits
    /// are output the export regenerates from the catalog, so the clone moves to the remote; when any was not, it stays
    /// and the divergence is logged as an error. Neither way throws, so a diverged clone never fails a shell start.
    /// The same holds when the move itself is refused (see <see cref="MoveToAsync"/>).
    /// </summary>
    private async Task<string?> WriterTargetAsync(string repoPath, string remote, CancellationToken cancellationToken)
    {
        var (ahead, behind) = await GitRemoteRefs.AheadBehindAsync(gitClient, repoPath, _options.Branch, cancellationToken);

        if (behind == 0)
        {
            ForgetReports(repoPath);
            return null;
        }

        if (ahead == 0)
            return remote;

        var foreignCommits = await ForeignCommitsAsync(repoPath, remote, cancellationToken);
        if (foreignCommits.Count == 0)
        {
            LogDiscardingExportCommits(repoPath, ahead, behind);
            return remote;
        }

        if (FirstReport(repoPath, Diverged))
            LogKeepingDivergedClone(repoPath, ahead, behind, foreignCommits);
        return null;
    }

    /// <summary>The commits this clone has and the remote lacks that the export did not make, as <c>{hash} {author name} &lt;{author email}&gt;</c>, with the committer's email when it differs.</summary>
    private async Task<IReadOnlyList<string>> ForeignCommitsAsync(string repoPath, string remote, CancellationToken cancellationToken)
    {
        var log = await gitClient.RunAsync(repoPath, cancellationToken, "log", $"--format=%h%x09{GitExportIdentity.LogFormat}", $"{remote}..HEAD");
        return log.Lines()
            .Select(line => line.Split('\t'))
            .Where(fields => !GitExportIdentity.IsExport(fields[1], fields[2], fields[3]))
            .Select(fields => $"{fields[0]} {fields[1]} <{fields[2]}>" + (fields[2] == fields[3] ? "" : $" (committed by {fields[3]})"))
            .ToList();
    }

    /// <summary>Whether <paramref name="problem"/> is reported for the clone for the first time since it was last found healthy.</summary>
    private static bool FirstReport(string repoPath, string problem) => ReportedClones.TryAdd(ReportKey(problem, repoPath), 0);

    private static void ForgetReports(string repoPath)
    {
        ReportedClones.TryRemove(ReportKey(Diverged, repoPath), out _);
        ReportedClones.TryRemove(ReportKey(MoveRefused, repoPath), out _);
    }

    private static string ReportKey(string problem, string repoPath) => $"{problem}|{repoPath}";

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

    private void LogMoveRefused(string repoPath, string target, Exception exception)
    {
        logger.LogError(
            exception,
            "The workflows clone at '{path}' could not move to {target}: the move would overwrite an uncommitted change outside the workflows path. " +
            "It was left as it is, so nothing is lost: the import reads it as it stands and the export's push is refused until the change is committed, stashed or discarded by hand.",
            repoPath, target);
    }
}
