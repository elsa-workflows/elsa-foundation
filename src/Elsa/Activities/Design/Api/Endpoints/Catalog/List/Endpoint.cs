using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Catalog.List;

[Get("/design/activities/catalog")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListActivityAuthoringCatalog, ActivityAuthoringCatalogView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "CatalogList";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityAuthoringCatalogView> HandleAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
