using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Layout;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution layout")]
public sealed class Endpoint(IActivityExecutionLayoutReader layout) : ApiEndpoint<GetActivityExecutionLayout, ActivityExecutionLayoutView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityLayout";

    public override async Task<ActivityExecutionLayoutView> HandleAsync(GetActivityExecutionLayout request, CancellationToken cancellationToken) =>
        await layout.ReadAsync(request, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}
