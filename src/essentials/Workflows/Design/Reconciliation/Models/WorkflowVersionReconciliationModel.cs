using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Reconciliation.Models;

/// <summary>
/// One reconciliation-source entry. Carries the natural-key identifiers a source contributes
/// for a single workflow-definition version. The reconciler maps this into the
/// <c>WorkflowDefinition</c> + <c>WorkflowDefinitionVersion</c> entity pair before upserting.
/// </summary>
/// <param name="ContentHash">
/// Optional canonical content hash (hex) of <paramref name="State"/> the source carries ahead of a
/// persisted home (spec 085 FR-007; the persisted provenance/hash fields are FR-016a's allocation).
/// The reconciler's Model X mismatch tripwire recomputes from <see cref="State"/>, so this is
/// forward-compat metadata rather than a required input today.
/// </param>
/// <param name="Deleted">
/// Definition-level soft-delete intent (spec 085 FR-008), sourced from a source's mutable
/// definition metadata (git's <c>definition.json</c>). Latest-wins; never deletes a version row.
/// </param>
public sealed record WorkflowVersionReconciliationModel(
    string? DefinitionId,
    string Name,
    string? Description,
    string Version,
    WorkflowDefinitionState State,
    DateTimeOffset? SourceCreatedAt = null,
    string? ContentHash = null,
    bool Deleted = false
);
