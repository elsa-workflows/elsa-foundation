namespace Elsa.Workflows.Design.Reconciliation.Git.Contracts;

/// <summary>
/// The Writer-only export reconciler (ADR 0034 D4): a set-diff sweep that makes git's version files
/// match the catalog's version set. Every decision comes from git state, never from files on disk (#2197):
/// a catalog version whose <c>versions/{semver}.json</c> is absent from the HEAD tree is written and committed,
/// and one present there is skipped; <c>definition.json</c> is committed when the HEAD copy differs from the
/// catalog's metadata; a version without its tag in HEAD's history is tagged; and under the Immediate push mode
/// the branch is pushed whenever HEAD is ahead of the remote, whether or not this pass committed anything. So a
/// rerun after a stop at any point completes the work. Idempotent; a no-op when everything is present.
/// Loop-avoidance is structural — writes only absent files — so it never re-exports git-sourced versions.
/// </summary>
public interface IGitWorkflowExporter
{
    Task ExportAsync(CancellationToken cancellationToken);
}
