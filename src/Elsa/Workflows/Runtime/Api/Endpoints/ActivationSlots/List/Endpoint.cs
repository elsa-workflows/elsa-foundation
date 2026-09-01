using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivationSlots.List;

[Get("runtime/workflows/activation-slots/{definitionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow activation slots")]
public sealed class Endpoint(WorkflowActivationSlotInspectionService inspection)
    : ApiEndpoint<ListWorkflowActivationSlots, WorkflowActivationSlotListView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListWorkflowActivationSlots";

    public override Task<WorkflowActivationSlotListView> HandleAsync(
        ListWorkflowActivationSlots request,
        CancellationToken cancellationToken) => inspection.ListAsync(request, cancellationToken);
}
