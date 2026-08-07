using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;

namespace Elsa.Workflows.Design.Api.Endpoints.Structures;

internal sealed class List(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<ActivityStructuresResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.Structures);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new ListActivityStructures(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
