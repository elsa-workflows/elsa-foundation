using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.GetByIdempotencyKey;

[Get("/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDraftTestRunService testRuns) : ApiEndpoint<GetActivityDraftTestRunByIdempotencyKey, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityDraftTestRunByIdempotencyKeyEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(GetActivityDraftTestRunByIdempotencyKey request, CancellationToken cancellationToken) =>
        testRuns.GetByIdempotencyKeyAsync(request.DraftId, request.IdempotencyKey, cancellationToken);
}
