using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityPublications.GetReceipt;

[Get("/design/activities/publications/{idempotencyKey}")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityPublicationReceipt, ActivityPublicationReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityPublicationReceiptEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity publication receipt lookup was rejected")));
    }

    public override Task<ActivityPublicationReceiptView> HandleAsync(GetActivityPublicationReceipt request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
