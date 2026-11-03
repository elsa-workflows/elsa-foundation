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

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.Replace;

[Put("templates/{templateId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(ICredentialLiteralValidator credentials, IWorkflowTemplateStore? store = null) : ApiEndpoint<SaveWorkflowTemplate, WorkflowTemplateView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "WorkflowTemplatesReplace"; options.Accepts = ["application/json"]; }
    public override async Task<WorkflowTemplateView> HandleAsync(SaveWorkflowTemplate request, CancellationToken cancellationToken)
    {
        var templates = TemplateStoreAvailability.Require(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentNullException.ThrowIfNull(request.Content);
        ArgumentNullException.ThrowIfNull(request.Content.State);
        var content = request.Content.ToContent();
        await credentials.AdmitAsync(content.State, cancellationToken);
        return WorkflowTemplateView.From(await templates.SaveAsync(DesignOperationKey.CreateOrGenerate(request.OperationKey), request.TemplateId,
            request.Name, request.Description, content, request.ExpectedRevision, cancellationToken));
    }
}
