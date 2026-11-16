using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.ValidateCandidate;

[Post("/design/activities/drafts/{draftId}/candidate-validation")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<ValidateReusableActivityCandidate, ActivityCandidateValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsValidateCandidate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityCandidateValidationView> HandleAsync(ValidateReusableActivityCandidate command, CancellationToken cancellationToken) =>
        service.ValidateCandidateAsync(command, cancellationToken);
}
