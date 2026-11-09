using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Read-only projections of the runtime activation authority.</summary>
public sealed class WorkflowActivationSlotInspectionService(
    IWorkflowActivationAuthority authority,
    IOptions<ActivationChannelOptions>? channelOptions = null)
{
    private ActivationChannelOptions Channels => channelOptions?.Value ?? new ActivationChannelOptions();

    public async Task<WorkflowActivationSlotListView> ListAsync(
        ListWorkflowActivationSlots request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slots = await authority.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        var options = Channels;
        var channels = options.Channels.OrderBy(channel => channel.Name, StringComparer.Ordinal)
            .Select(ChannelView).ToArray();
        return new WorkflowActivationSlotListView(slots.Select(slot => View(slot, channels)).ToArray())
        {
            Channels = channels,
            AllowAdHocChannels = options.AllowAdHocChannels
        };
    }

    public async Task<WorkflowActivationSlotView> GetAsync(
        GetWorkflowActivationSlot request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slot = await authority.FindAsync(request.DefinitionId, request.SlotName, cancellationToken);
        return slot is null
            ? throw EntityNotFoundException.ForEntity(
                typeof(WorkflowActivationSlot),
                WorkflowActivationSlotIdentity.Create(request.DefinitionId, request.SlotName))
            : View(slot, Channels.Channels.Select(ChannelView).ToArray());
    }

    private static ActivationChannelView ChannelView(ActivationChannelDefinition channel) =>
        new(channel.Name, string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.Name : channel.DisplayName, channel.Description);

    private static WorkflowActivationSlotView View(WorkflowActivationSlot slot, IReadOnlyCollection<ActivationChannelView> channels) =>
        WorkflowActivationSlotView.From(slot) with
        {
            Channel = channels.FirstOrDefault(channel => StringComparer.Ordinal.Equals(channel.Name, slot.SlotName))
        };
}
