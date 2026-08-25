using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.ListVersions;

[Get("/design/activities/definitions/{definitionId}/versions")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IActivityDefinitionManagementProjectionService service) : ApiEndpoint<ListReusableActivityVersions, ActivityManagementPageView<ReusableActivityVersionManagementView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsListVersions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> HandleAsync(ListReusableActivityVersions request, CancellationToken cancellationToken) =>
        service.ListVersionsAsync(request, cancellationToken);
}
