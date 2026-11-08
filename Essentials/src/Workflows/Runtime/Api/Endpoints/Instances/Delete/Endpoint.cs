using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.Delete;

[Delete("runtime/workflows/instances/{workflowExecutionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)]
[RuntimeProblems("deleting workflow instance")]
public sealed class Endpoint(WorkflowInstanceDeletionService deletion) : ApiEndpoint<DeleteWorkflowInstance, WorkflowInstanceDeletionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "DeleteInstance";

    public override Task<WorkflowInstanceDeletionView> HandleAsync(DeleteWorkflowInstance request, CancellationToken cancellationToken) =>
        deletion.DeleteAsync(request.WorkflowExecutionId, cancellationToken);
}
