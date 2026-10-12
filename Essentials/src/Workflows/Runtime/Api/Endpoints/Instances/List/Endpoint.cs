using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints.Instances;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.List;

[Get("runtime/workflows/instances")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow instances")]
public sealed class Endpoint(IWorkflowInstanceListService instances) : ApiEndpoint<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ListInstances";
        options.Convention(builder => builder.AddEndpointMetadata(WorkflowInstanceListQueryBinding.IncidentHealthParameter));
    }

    public override async Task<IReadOnlyCollection<WorkflowInstanceSummaryView>> HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        request = request.WithIncidentHealthQuery(HttpContext.Request.Query);
        var result = await instances.ListAsync(request.ForLegacyArray(), cancellationToken);
        return result.Items;
    }
}
