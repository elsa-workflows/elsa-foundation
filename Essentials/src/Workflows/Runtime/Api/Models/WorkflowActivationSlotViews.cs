using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json.Serialization;

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
    /// <summary>Installation-declared metadata, absent for an ad-hoc activation lane.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActivationChannelView? Channel { get; init; }

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

/// <summary>The shell-wide channel declaration, independent of any workflow definition's slot.</summary>
public sealed record ActivationChannelView(string Name, string DisplayName, string? Description);

public sealed record WorkflowActivationSlotListView(IReadOnlyCollection<WorkflowActivationSlotView> Items)
{
    /// <summary>All declared channels, including those that have no slot for this definition yet.</summary>
    public IReadOnlyCollection<ActivationChannelView> Channels { get; init; } = [];

    public bool AllowAdHocChannels { get; init; } = true;
}
