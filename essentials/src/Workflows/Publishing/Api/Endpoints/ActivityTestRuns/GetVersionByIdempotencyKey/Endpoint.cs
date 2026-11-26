using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using MinimalEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.GetVersionByIdempotencyKey;

[Get("/publishing/activity-versions/{versionId}/test-runs/idempotency/{idempotencyKey}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDraftTestRunService testRuns) : ApiEndpoint<GetActivityVersionTestRunByIdempotencyKey, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityVersionTestRunByIdempotencyKeyEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity version Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(GetActivityVersionTestRunByIdempotencyKey request, CancellationToken cancellationToken) =>
        testRuns.GetVersionByIdempotencyKeyAsync(request.VersionId, request.IdempotencyKey, cancellationToken);
}