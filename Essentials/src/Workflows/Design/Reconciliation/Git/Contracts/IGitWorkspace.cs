namespace Elsa.Workflows.Design.Reconciliation.Git.Contracts;

/// <summary>
/// Owns the local working clone of the configured repository and keeps it current per role (ADR 0034
/// D11). Called lazily by the source and the exporter before they touch files, so a re-run picks up
/// remote changes and a fresh catalog self-seeds at bootstrap.
/// </summary>
public interface IGitWorkspace
{
    /// <summary>
    /// Clone-if-absent, then integrate per role. A Consumer
    /// fetches and <c>reset --hard</c> to the remote branch. A Writer fetches and decides from how its branch
    /// stands against the remote (#2197): up to date or only ahead, it keeps its unpushed export commits; only
    /// behind, it fast-forwards; diverged, it resets to the remote when every commit the remote lacks was made
    /// by the export, judged by author name, author email and committer email (the export regenerates them from the
    /// catalog), and otherwise stays as it is and logs an error, once per clone until it is found healthy again (up to
    /// date or only ahead), so a problem that comes back after a repair is logged again. The clone also stays, with an
    /// error, when moving it would overwrite an uncommitted change outside the workflows path. None of these throws, so a
    /// clone that cannot be moved never fails a shell start. Under the workflows path a Writer's
    /// working tree is left at the commit it settles on: uncommitted residue of an interrupted export is discarded,
    /// so callers read only what is committed. Uncommitted changes elsewhere in the clone are kept. Returns the
    /// absolute repository path.
    /// </summary>
    Task<string> EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Absolute path to the working clone: an explicit <c>LocalCachePath</c>, else the clone in the shell's clone slot
    /// (#2197), which the first use takes.
    /// </summary>
    string RepositoryPath { get; }

    /// <summary>
    /// Runs a git command that reaches the remote, such as a fetch or a push, in the clone with the configured
    /// credentials (FR-013): per-invocation <c>-c …</c> arguments, and for a token an environment variable of that one
    /// git process that a credential helper reads, so nothing secret rides the command line or is written to disk
    /// (#2197). The exporter fetches its export branch and pushes through it.
    /// </summary>
    Task<string> RunRemoteAsync(CancellationToken cancellationToken, params string[] arguments);
}
