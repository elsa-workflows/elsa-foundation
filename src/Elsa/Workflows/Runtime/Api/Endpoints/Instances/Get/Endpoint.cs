using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.Get;

[Get("runtime/workflows/instances/{workflowExecutionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("reading workflow instance")]
public sealed class Endpoint(IWorkflowInstanceDetailsService details) : ApiEndpoint<GetWorkflowInstance, WorkflowInstanceDetailsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetInstance";

    public override async Task<WorkflowInstanceDetailsView> HandleAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        // An unparseable page size reads as absent and falls back to the store default, as the
        // mapper's lenient helper did; the binder alone would bind garbage to zero.
        request = request with { ActivityPageSize = RuntimeQuery.Int(HttpContext, "activityPageSize") ?? RuntimeStorePageRequest.DefaultLimit };
        return await details.GetAsync(request, cancellationToken) ?? throw new RuntimeResourceMissingSignal();
    }
}
