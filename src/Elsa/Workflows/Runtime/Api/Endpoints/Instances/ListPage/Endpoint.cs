using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.ListPage;

[Get("runtime/workflows/instances/page")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow instances")]
public sealed class Endpoint(IWorkflowInstanceListService instances) : ApiEndpoint<ListWorkflowInstances, WorkflowInstanceListView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListInstancesPage";

    public override Task<WorkflowInstanceListView> HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
        instances.ListAsync(request, cancellationToken);
}
