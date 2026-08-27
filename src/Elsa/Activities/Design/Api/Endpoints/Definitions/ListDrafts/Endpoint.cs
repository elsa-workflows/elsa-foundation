using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.ListDrafts;

[Get("/design/activities/definitions/{definitionId}/drafts")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IActivityDefinitionManagementProjectionService service) : ApiEndpoint<ListReusableActivityDrafts, ActivityManagementPageView<ReusableActivityDraftManagementView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsListDrafts";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> HandleAsync(ListReusableActivityDrafts request, CancellationToken cancellationToken) =>
        service.ListDraftsAsync(request, cancellationToken);
}
