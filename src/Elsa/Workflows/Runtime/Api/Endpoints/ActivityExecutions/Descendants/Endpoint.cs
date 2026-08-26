using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Descendants;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution descendants")]
public sealed class Endpoint(IActivityExecutionDescendantsReader hierarchy) : ApiEndpoint<GetActivityExecutionDescendants, ActivityExecutionHierarchyPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityDescendants";

    public override async Task<ActivityExecutionHierarchyPageView> HandleAsync(GetActivityExecutionDescendants request, CancellationToken cancellationToken) =>
        await hierarchy.ReadAsync(request, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}
