using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.History.Event;

[Get("runtime/workflows/instances/{workflowExecutionId}/history/events/{historyEventId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading a run history event")]
public sealed class Endpoint(RuntimeHistoryInspectionService history) : ApiEndpoint<GetRuntimeHistoryEvent, RuntimeHistoryEventDetailsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimeHistoryEvent";
    public override Task<RuntimeHistoryEventDetailsView> HandleAsync(GetRuntimeHistoryEvent request, CancellationToken cancellationToken) =>
        history.FindEventAsync(request, cancellationToken);
}
