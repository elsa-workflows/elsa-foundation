using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Get;

[Get("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetReusableActivityDraft, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ReusableActivityDraftView> HandleAsync(GetReusableActivityDraft request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
