using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Reconciliation.Models;

/// <summary>
/// One reconciliation-source entry. Carries the natural-key identifiers a source contributes
/// for a single workflow-definition version. The reconciler maps this into the
/// <c>WorkflowDefinition</c> + <c>WorkflowDefinitionVersion</c> entity pair before upserting.
/// </summary>
public sealed record WorkflowVersionReconciliationModel(
    string? DefinitionId,
    string Name,
    string? Description,
    int Version,
    WorkflowDefinitionState State,
    DateTimeOffset? SourceCreatedAt = null
);
