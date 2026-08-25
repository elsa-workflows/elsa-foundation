using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.ListPage;

[Get("runtime/workflows/instances/page")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow instances")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListWorkflowInstances, WorkflowInstanceListView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListInstancesPage";

    public override Task<WorkflowInstanceListView> HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
