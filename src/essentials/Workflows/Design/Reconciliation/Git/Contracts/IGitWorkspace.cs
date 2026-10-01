namespace Elsa.Workflows.Design.Reconciliation.Git.Contracts;

/// <summary>
/// Owns the local working clone of the configured repository and keeps it current per role (ADR 0034
/// D11). Called lazily by the source and the exporter before they touch files, so a re-run picks up
/// remote changes and a fresh catalog self-seeds at bootstrap.
/// </summary>
public interface IGitWorkspace
{
    /// <summary>
    /// Clone-if-absent, apply credentials into the clone's git config, then integrate per role. A Consumer
    /// fetches and <c>reset --hard</c> to the remote branch. A Writer fetches and decides from how its branch
    /// stands against the remote (#2197): up to date or only ahead, it keeps its unpushed export commits; only
    /// behind, it fast-forwards; diverged, it resets to the remote when every commit the remote lacks was made
    /// by the export (the export regenerates them from the catalog), and otherwise stays as it is and logs an
    /// error. A diverged clone never throws, so it never fails a shell start. Under the workflows path a Writer's
    /// working tree is left at the commit it settles on: uncommitted residue of an interrupted export is discarded,
    /// so callers read only what is committed. Uncommitted changes elsewhere in the clone are kept. Returns the
    /// absolute repository path.
    /// </summary>
    Task<string> EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>Absolute path to the working clone (valid after <see cref="EnsureReadyAsync"/>).</summary>
    string RepositoryPath { get; }

    /// <summary>
    /// The per-invocation <c>-c …</c> credential arguments (e.g. <c>core.sshCommand</c>) to prefix onto
    /// network git commands so nothing secret rides the command line (FR-013). Valid after
    /// <see cref="EnsureReadyAsync"/>; the exporter reuses them for its push.
    /// </summary>
    IReadOnlyList<string> CredentialArgs { get; }
}
