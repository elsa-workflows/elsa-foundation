namespace Elsa.Workflows.Design.Reconciliation.Core;

/// <summary>
/// Provenance of one contributed workflow-version envelope: which source contributed it, what it
/// identified, and whether that source asked for post-reconcile publication. Source identity is not
/// persisted on workflow-design entities (Unit D's allocation), so claims are the only carrier that
/// survives past the contribution phase — the aggregating handler records one claim per entry beside
/// the version it adds, and the reconciler republishes the set on
/// <see cref="OnWorkflowVersionsReconciled"/> after a successful pass.
/// </summary>
/// <param name="DefinitionId">The resolved definition id (post-factory, so generated ids are final).</param>
/// <param name="Version">The author-controlled SemVer of the contributed envelope.</param>
/// <param name="SemVerSortKey">The ordinal-comparable sort key of <paramref name="Version"/>.</param>
/// <param name="SourceId">The contributing source's identity.</param>
/// <param name="SourceKind">The contributing source's kind (e.g. <c>"Json"</c>, <c>"git"</c>).</param>
/// <param name="PublishRequested">Snapshot of the source's <c>RequestsPublication</c> at contribution time.</param>
/// <param name="Deleted">The envelope's soft-delete marker — deleted definitions are never published.</param>
public sealed record WorkflowVersionSourceClaim(
    string DefinitionId,
    string Version,
    string SemVerSortKey,
    string SourceId,
    string SourceKind,
    bool PublishRequested,
    bool Deleted);
