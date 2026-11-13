using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.History.Page;

[Get("runtime/workflows/instances/{workflowExecutionId}/history")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading run history", History = true)]
public sealed class Endpoint(RuntimeHistoryInspectionService history) : ApiEndpoint<GetRuntimeHistory, RuntimeHistoryPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimeHistory";
    public override Task<RuntimeHistoryPageView> HandleAsync(GetRuntimeHistory request, CancellationToken cancellationToken) =>
        history.ReadAsync(request, cancellationToken);
}
