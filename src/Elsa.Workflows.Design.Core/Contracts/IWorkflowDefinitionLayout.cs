namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Tier-1 read contract over a workflow definition's designer layout. Unified surface for
/// design-time consumers reading layout for either a <c>WorkflowDefinitionVersion</c> (via
/// <c>WorkflowDefinitionVersionLayout</c>) or a <c>WorkflowDefinitionDraft</c> (via
/// <c>WorkflowDefinitionDraftLayout</c>). Lets consumers read without depending on
/// <c>*.Persistence.Core</c> and without branching on the parent type. Unit C FR-007.
/// </summary>
public interface IWorkflowDefinitionLayout
{
    string Id { get; }
    IReadOnlyList<IDesignMetadataRecord> Records { get; }
}

/// <summary>
/// One layout record per placed activity node. Keyed by <c>NodeId</c> — the join key into
/// the parent's <c>WorkflowDefinitionState.Activities[*].NodeId</c>. Unit C FR-006 sub-shape.
/// </summary>
public interface IDesignMetadataRecord
{
    string NodeId { get; }
    double X { get; }
    double Y { get; }
    double? Width { get; }
    double? Height { get; }
    IReadOnlyDictionary<string, object?>? AdditionalProperties { get; }
}
