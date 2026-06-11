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
/// <c>Flowchart.Activities</c>. The node itself is not a universal composition carrier.
/// </remarks>
public sealed record ActivityNode(
    string NodeId,
    string ActivityVersionId,
    IEnumerable<ArgumentState> Inputs,
    IEnumerable<ArgumentState> Outputs,
    IEnumerable<ActivityChildSlot>? ChildSlots = null,
    IEnumerable<ActivityConnectionSlot>? ConnectionSlots = null
);

/// <summary>
/// Activity-specific child activity slot. The slot name is part of the owning activity contract.
/// </summary>
public sealed record ActivityChildSlot(
    string Name,
    IEnumerable<ActivityNode> Activities,
    IReadOnlyDictionary<string, string>? Metadata = null
);

/// <summary>
/// Activity-specific connection slot. Flowchart-style activities own these slots; primitive
/// activities and list/branch composites usually do not.
/// </summary>
public sealed record ActivityConnectionSlot(
    string Name,
    IEnumerable<ActivityConnection> Connections
);

public static class ActivityChildSlotNames
{
    public const string Activities = nameof(Activities);
    public const string Connections = nameof(Connections);
    public const string Body = nameof(Body);
    public const string Then = nameof(Then);
    public const string Else = nameof(Else);
    public const string Root = nameof(Root);
}

public static class ActivityChildSlotMetadataKeys
{
    public const string StartActivityNodeId = nameof(StartActivityNodeId);
}
