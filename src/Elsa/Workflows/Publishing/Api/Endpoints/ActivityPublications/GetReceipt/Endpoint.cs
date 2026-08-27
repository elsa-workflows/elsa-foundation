using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Builder;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityPublications.GetReceipt;

[Get("/design/activities/publications/{idempotencyKey}")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IActivityDefinitionPublisher publisher) : ApiEndpoint<GetActivityPublicationReceipt, ActivityPublicationReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityPublicationReceiptEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity publication receipt lookup was rejected")));
    }

    public override async Task<ActivityPublicationReceiptView> HandleAsync(GetActivityPublicationReceipt request, CancellationToken cancellationToken) =>
        ActivityPublicationReceiptView.From(
            await publisher.GetReceiptAsync(request.IdempotencyKey, cancellationToken));
}
