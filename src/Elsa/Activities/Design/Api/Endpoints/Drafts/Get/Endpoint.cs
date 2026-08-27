using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Get;

[Get("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<GetReusableActivityDraft, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ReusableActivityDraftView> HandleAsync(GetReusableActivityDraft request, CancellationToken cancellationToken) =>
        service.GetDraftAsync(request, cancellationToken);
}
