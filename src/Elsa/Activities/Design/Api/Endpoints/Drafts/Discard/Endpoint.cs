using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Discard;

[Delete("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<DiscardReusableActivityDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsDiscard";
        options.Accepts = ["*/*", "application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task HandleAsync(DiscardReusableActivityDraft command, CancellationToken cancellationToken) =>
        service.DiscardDraftAsync(command, cancellationToken);
}
