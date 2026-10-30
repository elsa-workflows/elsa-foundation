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

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.Delete;

[Post("templates/{templateId}/delete")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(IWorkflowTemplateStore store) : ApiEndpoint<DeleteWorkflowTemplate, bool>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "WorkflowTemplatesDelete"; options.Accepts = ["application/json"]; }
    public override async Task<bool> HandleAsync(DeleteWorkflowTemplate request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(DesignOperationKey.CreateOrGenerate(request.OperationKey), request.TemplateId, request.ExpectedRevision, cancellationToken);
        return true;
    }
}
