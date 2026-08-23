using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityDrafts.Publish;

[Post("/design/activities/drafts/{draftId}/publish")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<PublishActivityDraft, ActivityPublicationReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PublishActivityDraftEndpoint";
        options.Accepts = ["application/json"];
        // A literal null body publishes with route-supplied identity alone, as the published contract allows.
        options.BodyMode = EndpointBodyMode.OptionalWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
        options.DocumentedStatus = StatusCodes.Status200OK;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity publication was rejected")));
    }

    public override async Task<ActivityPublicationReceiptView> HandleAsync(PublishActivityDraft request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(request, cancellationToken);
        HttpContext.Response.Headers.Location = $"/design/activities/publications/{Uri.EscapeDataString(response.IdempotencyKey)}";
        return response;
    }
}
