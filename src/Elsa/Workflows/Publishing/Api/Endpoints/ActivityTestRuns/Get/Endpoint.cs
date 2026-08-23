using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.Get;

[Get("/publishing/activity-test-runs/{testRunId}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityDraftTestRun, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetActivityDraftTestRunEndpoint";
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(GetActivityDraftTestRun request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
