using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.List;

[Get("/design/activities/definitions")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IActivityDefinitionManagementProjectionService service) : ApiEndpoint<ListReusableActivityDefinitions, ActivityManagementPageView<ReusableActivityDefinitionManagementView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsList";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> HandleAsync(ListReusableActivityDefinitions request, CancellationToken cancellationToken) =>
        service.ListDefinitionsAsync(request, cancellationToken);
}
