using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Validate;

[Post("/design/activities/drafts/{draftId}/validate")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<ValidateReusableActivityDraft, ActivityDraftValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsValidate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityDraftValidationView> HandleAsync(ValidateReusableActivityDraft command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
