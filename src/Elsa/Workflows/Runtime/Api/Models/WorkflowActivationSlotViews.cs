using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>The runtime-owned state of one named workflow activation lane.</summary>
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
