using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Incidents.List;

[Get("runtime/workflows/instances/{workflowExecutionId}/incidents")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow incidents")]
public sealed class Endpoint(IWorkflowIncidentListService incidents) : ApiEndpoint<ListIncidents, ListIncidentsResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListIncidents";

    public override async Task<ListIncidentsResponse> HandleAsync(ListIncidents request, CancellationToken cancellationToken)
    {
        var result = await incidents.ListAsync(request, cancellationToken);
        return result.WorkflowExists ? result : throw new RuntimeResourceMissingSignal();
    }
}
