using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Dispatches.Redrive;

[Post("runtime/workflows/dispatches/{dispatchId}/redrive")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<RedriveWorkflowDispatch, WorkflowDispatchRedriveView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "RedriveDispatch";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override Task<WorkflowDispatchRedriveView> HandleAsync(RedriveWorkflowDispatch request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
