using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Versions.ValidateCandidate;

[Post("/design/activities/versions/{versionId}/candidate-validation")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<ValidateReusableActivityVersionCandidate, ActivityCandidateValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsValidateCandidate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityCandidateValidationView> HandleAsync(ValidateReusableActivityVersionCandidate command, CancellationToken cancellationToken) =>
        service.ValidateVersionCandidateAsync(command, cancellationToken);
}