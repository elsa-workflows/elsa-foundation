using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

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
/// Child activities and their relationships belong to <see cref="Structure"/> and are interpreted
/// by the owning activity module. Generic design-time traversal is derived from structure through
/// activity-specific projection handlers rather than stored directly on the node.
/// </remarks>
public sealed record ActivityNode(
    string NodeId,
    string ActivityVersionId,
    IEnumerable<ArgumentState> Inputs,
    IEnumerable<ArgumentState> Outputs,
    ActivityNodeStructure? Structure = null
);

/// <summary>
/// Non-persisted traversal projection of activity-specific children. The slot name is part of
/// the owning activity contract, but core does not interpret it.
/// </summary>
public sealed record ActivityChildProjection(
    string Name,
    IEnumerable<ActivityNode> Activities
);

/// <summary>
/// Disclosure-safe public-contract usage contributed by an activity-owned structure for one child.
/// </summary>
public sealed record ActivityChildContractMemberUsage(
    string NodeId,
    IReadOnlyCollection<ActivityContractMemberUsage> MemberUsage
);

/// <summary>
/// Activity-owned authored structure for one activity node.
/// </summary>
/// <remarks>
/// This is per-node authored content, not activity catalog metadata. Core stores and
/// round-trips the generic shape; the owning activity module owns the kind name,
/// schema version, payload, and consistency rules for projected children.
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
