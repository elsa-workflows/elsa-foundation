using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>GET publishing/incident-strategies — descriptors and the effective publishing default.</summary>
internal sealed class ListIncidentStrategies(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<IncidentStrategiesResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.IncidentStrategies);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new Requests.ListIncidentStrategies(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
