using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>
/// One named activation lane of a workflow definition, as the runtime holds it.
/// </summary>
/// <remarks>
/// <para>
/// The activation slot is a <b>runtime</b> concept (§E2.2): it answers "which activation is live for this
/// definition, and which source owns it". It deliberately carries no publication state. A publication is one
/// possible <em>reason</em> a slot is occupied, and it exists only on an engine that composed publishing —
/// enriching a slot with its <c>PublicationRecord</c> stays behind in <c>Elsa.Workflows.Publishing.Api</c>
/// (T117).
/// </para>
/// <para>
/// <see cref="SourceKind"/> and <see cref="SourceId"/> project the slot's explicit
/// <see cref="WorkflowActivationSource"/> ownership field. Ownership is never inferred from the shape of
/// <see cref="ActiveActivationId"/> here any more than it is in the ledger itself (FR-B-006, P3).
/// </para>
/// </remarks>
public sealed record WorkflowActivationSlotView(
    string SlotId,
    string DefinitionId,
    string SlotName,
    string? ActiveActivationId,
    string? SourceKind,
    string? SourceId,
    long Revision,
    DateTimeOffset UpdatedAt)
{
    public static WorkflowActivationSlotView From(WorkflowActivationSlot slot) =>
        new(
            slot.SlotId,
            slot.WorkflowDefinitionId,
            slot.SlotName,
            slot.ActiveActivationId,
            slot.Source?.Kind,
            slot.Source?.SourceId,
            slot.Revision,
            slot.UpdatedAt);
}

public sealed record WorkflowActivationSlotListView(IReadOnlyCollection<WorkflowActivationSlotView> Items);
