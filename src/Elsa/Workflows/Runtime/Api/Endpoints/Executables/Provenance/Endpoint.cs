using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.Provenance;

[Get("runtime/workflows/executables/{artifactId}/provenance")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IWorkflowExecutableInspector inspector) : ApiEndpoint<GetWorkflowExecutableProvenance, ExecutableProvenanceView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetExecutableProvenance";

    public override Task<ExecutableProvenanceView> HandleAsync(GetWorkflowExecutableProvenance request, CancellationToken cancellationToken) =>
        inspector.GetProvenanceAsync(request, cancellationToken);
}
