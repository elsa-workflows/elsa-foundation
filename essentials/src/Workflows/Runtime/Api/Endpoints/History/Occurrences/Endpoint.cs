using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.History.Occurrences;

[Get("runtime/workflows/instances/{workflowExecutionId}/history/occurrences")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading recorded activity occurrences", History = true)]
public sealed class Endpoint(RuntimeHistoryInspectionService history) : ApiEndpoint<GetRuntimeHistoryOccurrences, RuntimeHistoryOccurrencePageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimeHistoryOccurrences";
    public override Task<RuntimeHistoryOccurrencePageView> HandleAsync(GetRuntimeHistoryOccurrences request, CancellationToken cancellationToken) =>
        history.ReadOccurrencesAsync(request, cancellationToken);
}
