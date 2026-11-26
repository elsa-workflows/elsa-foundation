using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.ActivityDataContext;

[Post("/design/activities/drafts/{draftId}/activity-data/context")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint : ApiEndpoint<ActivityDataSelectionContextRequest, ActivityDataSelectionContextResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsActivityDataContext";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityDataSelectionContextResponse> HandleAsync(ActivityDataSelectionContextRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return HttpContext.RequestServices.GetRequiredService<ActivityDataSelectionContextService>().ResolveAsync(request, cancellationToken);
    }
}
