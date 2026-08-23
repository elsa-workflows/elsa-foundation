using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.GetByIdempotencyKey;

[Get("/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityDraftTestRunByIdempotencyKey, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityDraftTestRunByIdempotencyKeyEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(GetActivityDraftTestRunByIdempotencyKey request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
