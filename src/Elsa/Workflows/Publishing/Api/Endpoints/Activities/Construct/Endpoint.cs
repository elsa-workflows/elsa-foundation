using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Activities.Construct;

[Get("/publishing/activities/{activityId}/construct")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ConstructActivity, ConstructedActivityView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "Construct";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ConstructedActivityView> HandleAsync(ConstructActivity request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
