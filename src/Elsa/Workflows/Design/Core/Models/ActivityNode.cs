using System.Text.Json;

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
/// Child activities are exposed through activity-specific named slots such as
/// <c>Sequence.Activities</c>, <c>If.Then</c>, <c>ForEach.Body</c>, or
/// <c>Flowchart.Activities</c>. These slots are traversal projections only. Activity-owned
/// relationship semantics such as ordering, branch meaning, loop bodies, flowchart
/// connections, and start nodes belong to <see cref="Structure"/> and are interpreted by
/// the owning activity module.
/// </remarks>
public sealed record ActivityNode(
    string NodeId,
    string ActivityVersionId,
    IEnumerable<ArgumentState> Inputs,
    IEnumerable<ArgumentState> Outputs,
    IEnumerable<ActivityChildSlot>? ChildSlots = null,
    ActivityNodeStructure? Structure = null
);

/// <summary>
/// Traversal projection of activity-specific children. The slot name is part of the owning
/// activity contract, but core does not interpret it.
/// </summary>
public sealed record ActivityChildSlot(
    string Name,
    IEnumerable<ActivityNode> Activities
);

/// <summary>
/// Activity-owned authored structure for one activity node.
/// </summary>
/// <remarks>
/// This is per-node authored content, not activity catalog metadata. Core stores and
/// round-trips the generic shape; the owning activity module owns the kind name,
/// schema version, payload, and consistency rules against projected child slots.
/// </remarks>
public sealed record ActivityNodeStructure
{
    public ActivityNodeStructure(string kind, string schemaVersion, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("An activity node structure kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("An activity node structure schema version is required.", nameof(schemaVersion));
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException("An activity node structure payload is required.", nameof(payload));

        Kind = kind;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    public string Kind { get; }
    public string SchemaVersion { get; }
    public JsonElement Payload { get; }
}
