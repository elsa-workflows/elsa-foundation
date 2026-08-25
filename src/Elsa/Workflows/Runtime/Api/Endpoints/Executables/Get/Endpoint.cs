using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.Get;

[Get("runtime/workflows/executables/{artifactId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetWorkflowExecutable, WorkflowExecutableDetailsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetExecutable";

    public override Task<WorkflowExecutableDetailsView> HandleAsync(GetWorkflowExecutable request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
