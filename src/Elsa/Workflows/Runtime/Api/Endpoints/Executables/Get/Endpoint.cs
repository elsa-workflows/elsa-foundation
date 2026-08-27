using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.Get;

[Get("runtime/workflows/executables/{artifactId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IWorkflowExecutableInspector inspector) : ApiEndpoint<GetWorkflowExecutable, WorkflowExecutableDetailsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetExecutable";

    public override Task<WorkflowExecutableDetailsView> HandleAsync(GetWorkflowExecutable request, CancellationToken cancellationToken) =>
        inspector.GetAsync(request, cancellationToken);
}
