using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Dispatches.List;

[Get("runtime/workflows/dispatches")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow dispatches")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListWorkflowDispatches, IReadOnlyCollection<WorkflowDispatchView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListDispatches";

    public override Task<IReadOnlyCollection<WorkflowDispatchView>> HandleAsync(ListWorkflowDispatches request, CancellationToken cancellationToken)
    {
        // The continuation values are init-only properties outside the binding constructor; the
        // mapper bound them from the query directly and the endpoint keeps doing so.
        request = request with
        {
            AfterCreatedAt = RuntimeQuery.Date(HttpContext, "afterCreatedAt"),
            AfterDispatchId = RuntimeQuery.Value(HttpContext, "afterDispatchId")
        };
        return sender.Send(request, cancellationToken);
    }
}
