using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Forks.GetStatus;

[Get("/design/activities/forks/{idempotencyKey}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetReusableActivityForkStatus, ActivityForkReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ForksGetStatus";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ActivityForkReceiptView> HandleAsync(GetReusableActivityForkStatus request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
