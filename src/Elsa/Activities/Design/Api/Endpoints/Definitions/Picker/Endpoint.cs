using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Picker;

[Get("/design/activities/definitions/picker")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListRecommendedActivityDefinitions, RecommendedActivityDefinitionPageView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsPicker";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<RecommendedActivityDefinitionPageView> HandleAsync(ListRecommendedActivityDefinitions request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
