using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Primitives.Exceptions;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.List;

[Get("templates")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowTemplateStore? store = null) : ApiEndpoint<ListWorkflowTemplates, IReadOnlyCollection<WorkflowTemplateView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowTemplatesList";
    public override async Task<IReadOnlyCollection<WorkflowTemplateView>> HandleAsync(ListWorkflowTemplates request, CancellationToken cancellationToken) =>
        (await TemplateStoreAvailability.Require(store).ListAsync(cancellationToken)).Select(WorkflowTemplateView.From).ToArray();
}
