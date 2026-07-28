using System.Text.Json;

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
    IEnumerable<IDesignMetadataRecord> Records { get; }

    /// <summary>Gets optional author-facing presentation keyed by activity node id.</summary>
    IEnumerable<IActivityPresentationRecord> ActivityPresentation { get; }
}

/// <summary>
/// One layout record per placed activity node. Keyed by <c>NodeId</c> — the join key into
/// the parent's <c>WorkflowDefinitionState.RootActivity</c> tree. Unit C FR-006 sub-shape.
/// </summary>
public interface IDesignMetadataRecord
{
    string NodeId { get; }
    double X { get; }
    double Y { get; }
    double? Width { get; }
    double? Height { get; }

    /// <summary>
    /// Opaque, Studio-authored per-node layout metadata, stored verbatim as a <see cref="JsonElement"/>
    /// (ADR 0035 D3) — read as JSON, never indexed as a CLR dictionary.
    /// </summary>
    JsonElement? AdditionalProperties { get; }
}

/// <summary>Read-only presentation metadata for one activity occurrence.</summary>
public interface IActivityPresentationRecord
{
    string NodeId { get; }
    string? DisplayName { get; }
    string? Description { get; }
}
