using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Builder;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityDrafts.Preflight;

[Post("/design/activities/drafts/{draftId}/publication-preflight")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IActivityDefinitionPublisher publisher) : ApiEndpoint<PreflightActivityDraftPublication, ActivityPublicationPreflightView>
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
        publisher.PreflightAsync(
            new(
                request.DraftId,
                request.ExpectedDraftRevision,
                request.ExpectedDefinitionHeadVersionId)
            {
                Version = request.Version
            },
            cancellationToken);
}
