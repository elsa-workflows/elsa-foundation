using System.Text;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <summary>
/// The last-published record's home: <c>published-versions.json</c> on the <c>publish-state</c> branch of the remote
/// (spec 150 FR-014).
/// </summary>
public interface IPublishState
{
    /// <summary>
    /// The branch's commit on the remote, asked of the remote itself and made readable locally; null only when the
    /// remote answers that the branch does not exist.
    /// </summary>
    /// <exception cref="InvalidOperationException">The remote could not be asked. An unanswered question is never taken for an absent branch.</exception>
    string? FetchTip();

    /// <summary>The branch's revisions from <paramref name="tip"/> back to its first, newest first.</summary>
    IReadOnlyList<string> Revisions(string tip);

    /// <summary>The record at a revision of the branch.</summary>
    PublishedVersions Read(string revision);

    /// <summary>
    /// Commits the record as the branch's only file, on top of <paramref name="parent"/>, or as an orphan when it is
    /// null, and pushes that commit to the branch without force; returns the commit.
    /// </summary>
    /// <exception cref="InvalidOperationException">The push was rejected: the branch moved, or exists when an orphan was asked for.</exception>
    string Write(PublishedVersions record, string? parent, string message);
}

/// <summary>
/// <see cref="IPublishState"/> over a git remote. The workflow's checkout persists its <c>GITHUB_TOKEN</c> for the
/// remote, so a write-back is pushed with that token and starts no workflow run (FR-017). Pushes are never forced:
/// the branch's ruleset forbids it, and a rejected push is how a moved branch shows.
/// </summary>
/// <param name="directory">A directory inside the local repository.</param>
/// <param name="remote">The remote holding the branch.</param>
/// <param name="branch">The branch holding the record.</param>
public sealed class GitPublishState(string directory, string remote = GitPublishState.DefaultRemote, string branch = GitPublishState.DefaultBranch) : IPublishState
{
    public const string DefaultRemote = "origin";

    public const string DefaultBranch = "publish-state";

    /// <summary>
    /// The identity every record commit carries: the one GitHub attributes <c>GITHUB_TOKEN</c> pushes to. It is set on
    /// the command rather than read from configuration, so a write-back never fails after the packages were pushed for
    /// want of one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Identity = new Dictionary<string, string>
    {
        ["GIT_AUTHOR_NAME"] = "github-actions[bot]",
        ["GIT_AUTHOR_EMAIL"] = "41898282+github-actions[bot]@users.noreply.github.com",
        ["GIT_COMMITTER_NAME"] = "github-actions[bot]",
        ["GIT_COMMITTER_EMAIL"] = "41898282+github-actions[bot]@users.noreply.github.com"
    };

    private readonly GitRepository git = new(directory);

    private string BranchRef => $"refs/heads/{branch}";

    private string TrackingRef => $"refs/remotes/{remote}/{branch}";

    public string? FetchTip()
    {
        // --exit-code: 0 when the branch exists, 2 when the remote answered that it does not, anything else a failure.
        var listed = Git(["ls-remote", "--exit-code", "--heads", remote, BranchRef]);
        if (listed.ExitCode == 2)
            return null;

        Require(listed, $"ask {remote} for {branch}");
        Require(Git(["fetch", "--quiet", "--no-tags", remote, $"+{BranchRef}:{TrackingRef}"]), $"fetch {branch} from {remote}");
        return git.ResolveCommit(TrackingRef);
    }

    public IReadOnlyList<string> Revisions(string tip) =>
        Require(Git(["rev-list", "--first-parent", tip]), $"list the revisions of {branch}").Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public PublishedVersions Read(string revision) => PublishedVersions.Load(git, revision);

    public string Write(PublishedVersions record, string? parent, string message)
    {
        var blob = Require(Git(["hash-object", "-w", "--stdin"], Encoding.UTF8.GetBytes(record.Serialize())), "store the record").Text;
        var tree = Require(Git(["mktree"], Encoding.UTF8.GetBytes($"100644 blob {blob}\t{PublishedVersions.DefaultPath}\n")), "make the record's tree").Text;
        var commit = Require(
            Git(["commit-tree", tree, .. parent is null ? Array.Empty<string>() : ["-p", parent], "--no-gpg-sign", "-m", message], environment: Identity),
            "commit the record").Text;

        Require(Git(["push", "--quiet", "--no-verify", remote, $"{commit}:{BranchRef}"]),
            parent is null
                ? $"create {branch} on {remote}; it must not exist yet"
                : $"push the record to {branch} on {remote}; it must still be at {parent}");
        return commit;
    }

    private GitResult Git(string[] arguments, byte[]? input = null, IReadOnlyDictionary<string, string>? environment = null) =>
        GitProcess.Run(git.Directory, arguments, input, environment);

    private static GitResult Require(GitResult result, string what) =>
        result.ExitCode == 0 ? result : throw new InvalidOperationException($"Could not {what}: git exited with {result.ExitCode}. {result.Error}");
}
