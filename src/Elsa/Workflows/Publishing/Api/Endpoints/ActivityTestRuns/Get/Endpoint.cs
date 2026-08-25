using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.Get;

[Get("/publishing/activity-test-runs/{testRunId}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDraftTestRunService testRuns) : ApiEndpoint<GetActivityDraftTestRun, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityDraftTestRunEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(GetActivityDraftTestRun request, CancellationToken cancellationToken) =>
        testRuns.GetAsync(request.TestRunId, cancellationToken);
}
