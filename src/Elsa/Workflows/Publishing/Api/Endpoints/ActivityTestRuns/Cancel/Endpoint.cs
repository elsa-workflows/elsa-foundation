using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.Cancel;

[Post("/publishing/activity-test-runs/{testRunId}/cancel")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDraftTestRunService testRuns) : ApiEndpoint<CancelActivityDraftTestRun, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "CancelActivityDraftTestRunEndpoint";
        // The published operation reads nothing from the request body.
        options.BodyMode = EndpointBodyMode.None;
        options.SuccessStatus = StatusCodes.Status202Accepted;
        options.DocumentedStatus = StatusCodes.Status200OK;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(CancelActivityDraftTestRun request, CancellationToken cancellationToken) =>
        testRuns.CancelAsync(request.TestRunId, cancellationToken);
}
