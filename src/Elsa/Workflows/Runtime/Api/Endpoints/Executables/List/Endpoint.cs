using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.List;

[Get("runtime/workflows/executables")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("listing workflow executables")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListWorkflowExecutables, WorkflowExecutablesListView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListExecutables";

    public override Task<WorkflowExecutablesListView> HandleAsync(ListWorkflowExecutables request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
