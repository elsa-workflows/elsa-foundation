using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Read-only projections of the runtime activation ledger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only, and pinned so (T117, 2026-08-17).</b> There is deliberately no deactivation request, handler
/// or endpoint here, and adding one is a spec change rather than a natural extension of these reads.
/// <c>IWorkflowActivationCoordinator.DeactivateAsync</c> is an in-process contract whose only production caller
/// is publishing's unpublish handler; a runtime-only engine composes no publishing and therefore has no
/// external deactivation surface at all. That immutability is the point — such an engine re-reconciles through
/// a shell reload (FR-B-008), not through an operator mutating its ledger over HTTP.
/// </para>
/// </remarks>
public sealed class ListWorkflowActivationSlotsRequestHandler(IWorkflowActivationAuthority activationAuthority)
    : IRequestHandler<ListWorkflowActivationSlots, WorkflowActivationSlotListView>
{
    public async Task<WorkflowActivationSlotListView> Handle(
        ListWorkflowActivationSlots request,
        CancellationToken cancellationToken)
    {
        var slots = await activationAuthority.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return new WorkflowActivationSlotListView(slots.Select(WorkflowActivationSlotView.From).ToArray());
    }
}

public sealed class GetWorkflowActivationSlotRequestHandler(IWorkflowActivationAuthority activationAuthority)
    : IRequestHandler<GetWorkflowActivationSlot, WorkflowActivationSlotView>
{
    public async Task<WorkflowActivationSlotView> Handle(
        GetWorkflowActivationSlot request,
        CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.DefinitionId, request.SlotName, cancellationToken);
        return slot is null
            ? throw EntityNotFoundException.ForEntity(
                typeof(WorkflowActivationSlot),
                WorkflowActivationSlotIdentity.Create(request.DefinitionId, request.SlotName))
            : WorkflowActivationSlotView.From(slot);
    }
}
