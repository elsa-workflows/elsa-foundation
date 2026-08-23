using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.IncidentStrategies.List;

[Get("/publishing/incident-strategies")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListIncidentStrategies, IncidentStrategiesResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "ListIncidentStrategies";

    public override Task<IncidentStrategiesResponse> HandleAsync(ListIncidentStrategies request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
