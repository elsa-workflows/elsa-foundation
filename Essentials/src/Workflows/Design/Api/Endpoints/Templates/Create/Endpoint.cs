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

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.Create;

[Post("templates")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(IWorkflowTemplateStore store, ICredentialLiteralValidator credentials) : ApiEndpoint<SaveWorkflowTemplate, WorkflowTemplateView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "WorkflowTemplatesCreate"; options.Accepts = ["application/json"]; }
    public override async Task<WorkflowTemplateView> HandleAsync(SaveWorkflowTemplate request, CancellationToken cancellationToken)
    {
        if (request.TemplateId is not null) throw new ArgumentException("Creating a template cannot replace an existing identity.");
        ArgumentNullException.ThrowIfNull(request.Content);
        ArgumentNullException.ThrowIfNull(request.Content.State);
        var content = request.Content.ToContent();
        await credentials.AdmitAsync(content.State, cancellationToken);
        return WorkflowTemplateView.From(await store.SaveAsync(DesignOperationKey.CreateOrGenerate(request.OperationKey), null,
            request.Name, request.Description, content, null, cancellationToken));
    }
}
