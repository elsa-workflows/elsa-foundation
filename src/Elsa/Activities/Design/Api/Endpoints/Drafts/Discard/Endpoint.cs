using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Discard;

[Delete("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<DiscardReusableActivityDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsDiscard";
        options.Accepts = ["*/*", "application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task HandleAsync(DiscardReusableActivityDraft command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
