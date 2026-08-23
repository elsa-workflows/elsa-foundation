using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Activities.List;

[Get("/publishing/activities")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListConstructableActivities, IEnumerable<ConstructableActivityView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "List";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<IEnumerable<ConstructableActivityView>> HandleAsync(ListConstructableActivities request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
