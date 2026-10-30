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

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.Get;

[Get("templates/{templateId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowTemplateStore store) : ApiEndpoint<GetWorkflowTemplate, WorkflowTemplateView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowTemplatesGet";
    public override async Task<WorkflowTemplateView> HandleAsync(GetWorkflowTemplate request, CancellationToken cancellationToken) =>
        WorkflowTemplateView.From(await store.FindAsync(request.TemplateId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowTemplate), request.TemplateId));
}
