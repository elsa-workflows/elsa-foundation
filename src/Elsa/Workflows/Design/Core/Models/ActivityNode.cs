using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

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
)
{
    /// <summary>
    /// Declares an engine-owned operation that is authored as a normal graph node but does not
    /// reference an activity catalog version or activate a CLR activity at runtime.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthoredWorkflowIntrinsic? Intrinsic { get; init; }
}

/// <summary>
/// Portable authored contract for an engine intrinsic. The value type is pinned at authoring time
/// because no CLR activity input contract exists from which Publishing could infer it.
/// </summary>
public sealed record AuthoredWorkflowIntrinsic
{
    public AuthoredWorkflowIntrinsic(
        AuthoredWorkflowIntrinsicKind kind,
        TypeReference? valueType = null,
        VariableReference? variable = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Authored workflow intrinsic kind is not defined.");

        var writesVariable = kind is AuthoredWorkflowIntrinsicKind.Set or
            AuthoredWorkflowIntrinsicKind.Merge or
            AuthoredWorkflowIntrinsicKind.Reduce;
        if (writesVariable && variable is null)
            throw new ArgumentException($"Authored workflow intrinsic '{kind}' requires a variable target.", nameof(variable));
        if (!writesVariable && variable is not null)
            throw new ArgumentException($"Authored workflow intrinsic '{kind}' cannot carry a variable target.", nameof(variable));
        if (kind is not AuthoredWorkflowIntrinsicKind.Control && valueType is null)
            throw new ArgumentException($"Authored workflow intrinsic '{kind}' requires a portable value type.", nameof(valueType));
        if (kind is AuthoredWorkflowIntrinsicKind.Control && valueType is not null)
            throw new ArgumentException("A control intrinsic carries an outcome key rather than a workflow value type.", nameof(valueType));

        Kind = kind;
        ValueType = valueType;
        Variable = variable;
    }

    public AuthoredWorkflowIntrinsicKind Kind { get; }
    public TypeReference? ValueType { get; }
    public VariableReference? Variable { get; }
}

public enum AuthoredWorkflowIntrinsicKind
{
    Set,
    Merge,
    Reduce,
    Return,
    Control
}

/// <summary>
/// Non-persisted traversal projection of activity-specific children. The slot name is part of
/// the owning activity contract, but core does not interpret it.
/// </summary>
public sealed record ActivityChildProjection(
    string Name,
    IEnumerable<ActivityNode> Activities
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
