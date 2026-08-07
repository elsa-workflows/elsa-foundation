using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class SubmitSchema(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<WorkflowDefinitionSubmitSchemaView>
{
    public override void Configure()
    {
        Get(RouteConstants.DefinitionSubmitSchema);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new GetWorkflowDefinitionSubmitSchema(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
