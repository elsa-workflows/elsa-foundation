namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The placed-activity node on the design canvas. Identified within its owning Version/Draft
/// by <see cref="NodeId"/> (uniqueness scoped to the parent instance; carries across
/// Draft → Version promotion as a copy per Unit C FR-009a). References the activity catalog
/// via <see cref="ActivityVersionId"/> — a single stable string identifier (Unit B's catalog
/// row id), replacing the prior <c>(activityDefinitionId, version)</c> pair per Unit C FR-011.
/// Per-node argument state lives on <see cref="Inputs"/> / <see cref="Outputs"/>.
/// </summary>
/// <remarks>
/// Designer layout (position, size, canvas metadata) is NOT carried on the node — it lives
/// on the parent's <c>WorkflowDefinitionVersionLayout</c> / <c>WorkflowDefinitionDraftLayout</c>
/// sibling as a <c>DesignMetadataRecord</c> keyed by <see cref="NodeId"/> per Elsa §E2.9.2.
/// </remarks>
public sealed record ActivityNode(
    string NodeId,
    string ActivityVersionId,
    IEnumerable<ArgumentState> Inputs,
    IEnumerable<ArgumentState> Outputs,
    bool IsContainer,
    bool IsStart,
    bool IsTerminal,
    IEnumerable<ActivityNode> ChildActivities
);
