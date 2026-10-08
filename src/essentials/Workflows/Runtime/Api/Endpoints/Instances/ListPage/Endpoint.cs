using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints.Instances;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.ListPage;

[Get("runtime/workflows/instances/page")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow instances")]
public sealed class Endpoint(IWorkflowInstanceListService instances) : ApiEndpoint<ListWorkflowInstances, WorkflowInstanceListView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ListInstancesPage";
        options.Convention(builder => builder.AddEndpointMetadata(WorkflowInstanceListQueryBinding.IncidentHealthParameter));
    }

    public override Task<WorkflowInstanceListView> HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
        instances.ListAsync(request.WithIncidentHealthQuery(HttpContext.Request.Query), cancellationToken);
}
