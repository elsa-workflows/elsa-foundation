using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivationSlots.Get;

[Get("runtime/workflows/activation-slots/{definitionId}/{slotName}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("reading workflow activation slot", NotFoundArms = true)]
public sealed class Endpoint(WorkflowActivationSlotInspectionService inspection)
    : ApiEndpoint<GetWorkflowActivationSlot, WorkflowActivationSlotView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetWorkflowActivationSlot";

    public override Task<WorkflowActivationSlotView> HandleAsync(
        GetWorkflowActivationSlot request,
        CancellationToken cancellationToken) => inspection.GetAsync(request, cancellationToken);
}
