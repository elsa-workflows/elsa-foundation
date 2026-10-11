using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Read-only projections of the runtime activation authority.</summary>
public sealed class WorkflowActivationSlotInspectionService(IWorkflowActivationAuthority authority)
{
    public async Task<WorkflowActivationSlotListView> ListAsync(
        ListWorkflowActivationSlots request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slots = await authority.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return new WorkflowActivationSlotListView(slots.Select(WorkflowActivationSlotView.From).ToArray());
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
            : WorkflowActivationSlotView.From(slot);
    }
}
