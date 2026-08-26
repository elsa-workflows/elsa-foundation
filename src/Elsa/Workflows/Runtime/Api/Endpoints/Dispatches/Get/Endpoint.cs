using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Dispatches.Get;

[Get("runtime/workflows/dispatches/{dispatchId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("reading runtime resource")]
public sealed class Endpoint(IWorkflowDispatchInspectionService dispatches) : ApiEndpoint<GetWorkflowDispatch, WorkflowDispatchView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetDispatch";

    public override async Task<WorkflowDispatchView> HandleAsync(GetWorkflowDispatch request, CancellationToken cancellationToken)
    {
        return await dispatches.GetAsync(request, cancellationToken) ?? throw new RuntimeResourceMissingSignal();
    }
}
