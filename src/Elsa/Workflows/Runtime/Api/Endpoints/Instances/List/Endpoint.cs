using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.List;

[Get("runtime/workflows/instances")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow instances")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListInstances";

    public override async Task<IReadOnlyCollection<WorkflowInstanceSummaryView>> HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request.ForLegacyArray(), cancellationToken);
        return result.Items;
    }
}
