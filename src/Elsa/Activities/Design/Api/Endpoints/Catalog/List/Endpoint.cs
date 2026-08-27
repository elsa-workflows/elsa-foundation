using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Catalog.List;

[Get("/design/activities/catalog")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IActivityAuthoringCatalogReader service) : ApiEndpoint<ListActivityAuthoringCatalog, ActivityAuthoringCatalogView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "CatalogList";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityAuthoringCatalogView> HandleAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken) =>
        service.ListAsync(request, cancellationToken);
}
