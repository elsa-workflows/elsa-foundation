using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.StateView;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity state view")]
public sealed class Endpoint(IActivityExecutionStateViewService stateViews) : ApiEndpoint<GetActivityExecutionStateView, ActivityStateView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityStateView";

    public override async Task<ActivityStateView> HandleAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken) =>
        await stateViews.GetAsync(request, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}
