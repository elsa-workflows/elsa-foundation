using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Replace;

[Put("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<ReplaceReusableActivityDraft, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsReplace";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ReusableActivityDraftView> HandleAsync(ReplaceReusableActivityDraft command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
