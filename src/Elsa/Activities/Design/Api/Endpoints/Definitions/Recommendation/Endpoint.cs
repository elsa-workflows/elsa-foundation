using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Recommendation;

[Put("/design/activities/definitions/{definitionId}/recommendation")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityDefinitionRecommendationService service) : ApiEndpoint<SetRecommendedReusableActivityVersion, ActivityDefinitionRecommendationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsRecommendation";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityDefinitionRecommendationView> HandleAsync(SetRecommendedReusableActivityVersion command, CancellationToken cancellationToken) =>
        service.SetAsync(command, cancellationToken);
}
