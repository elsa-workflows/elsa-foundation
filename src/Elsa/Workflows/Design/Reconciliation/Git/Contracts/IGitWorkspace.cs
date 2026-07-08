namespace Elsa.Workflows.Design.Reconciliation.Git.Contracts;

/// <summary>
/// Owns the local working clone of the configured repository and keeps it current per role (ADR 0034
/// D11). Called lazily by the source and the exporter before they touch files, so a re-run picks up
/// remote changes and a fresh catalog self-seeds at bootstrap.
/// </summary>
public interface IGitWorkspace
{
    /// <summary>
    /// Clone-if-absent, apply credentials into the clone's git config, then integrate per role: a Writer
    /// fetches and fast-forward-only merges (a non-ff divergence — the D7 single-writer signal — throws;
    /// the Writer clone is never <c>reset --hard</c>); a Consumer fetches and <c>reset --hard</c> to the
    /// remote branch. Returns the absolute repository path.
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
