using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityTestRuns.Start;

[Post("/publishing/activity-drafts/{draftId}/test-runs")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<StartActivityDraftTestRun, ActivityDraftTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivityDraftTestRunEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status202Accepted;
        options.DocumentedStatus = StatusCodes.Status200OK;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity draft Test Run rejected")));
    }

    public override Task<ActivityDraftTestRunView> HandleAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
