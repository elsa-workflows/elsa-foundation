using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Get;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution")]
public sealed class Endpoint(IActivityExecutionInspectionService inspection) : ApiEndpoint<GetActivityExecution, ActivityExecutionInspectionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityExecution";

    public override async Task<ActivityExecutionInspectionView> HandleAsync(GetActivityExecution request, CancellationToken cancellationToken) =>
        await inspection.GetAsync(request, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}
