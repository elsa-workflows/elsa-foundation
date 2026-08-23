using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityDrafts.Preflight;

[Post("/design/activities/drafts/{draftId}/publication-preflight")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<PreflightActivityDraftPublication, ActivityPublicationPreflightView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PreflightActivityDraftPublicationEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity publication preflight was rejected")));
    }

    public override Task<ActivityPublicationPreflightView> HandleAsync(PreflightActivityDraftPublication request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
