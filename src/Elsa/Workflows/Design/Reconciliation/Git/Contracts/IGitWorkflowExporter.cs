namespace Elsa.Workflows.Design.Reconciliation.Git.Contracts;

/// <summary>
/// The Writer-only export reconciler (ADR 0034 D4): a set-diff sweep that makes git's version files
/// match the catalog's version set. For each catalog version it ensures <c>versions/{semver}.json</c>
/// exists (write + commit if absent; skip if present), refreshes <c>definition.json</c> on metadata
/// change, then pushes per the configured push mode. Idempotent; a no-op when everything is present.
/// Loop-avoidance is structural — writes only absent files — so it never re-exports git-sourced versions.
/// </summary>
public interface IGitWorkflowExporter
{
    Task ExportAsync(CancellationToken cancellationToken);
}
