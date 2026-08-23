using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Diff;

[Post("/design/activities/drafts/{draftId}/diff")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<PreviewActivityDraftDiff, ActivityVersionDiffView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsDiff";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityVersionDiffView> HandleAsync(PreviewActivityDraftDiff request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
