using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.StartVersion;

[Post("/publishing/activity-versions/{versionId}/test-runs")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDraftTestRunService testRuns) : ApiEndpoint<StartActivityVersionTestRun, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivityVersionTestRunEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status202Accepted;
        options.DocumentedStatus = StatusCodes.Status200OK;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity version Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(StartActivityVersionTestRun request, CancellationToken cancellationToken) =>
        testRuns.StartVersionAsync(request, cancellationToken);
}