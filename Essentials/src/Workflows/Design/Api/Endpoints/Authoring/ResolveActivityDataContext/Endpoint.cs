using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.ResolveActivityDataContext;

[Post("activity-data/context")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ActivityDataContextRequest, ActivityDataContextResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringResolveActivityDataContext";
        options.Accepts = ["application/json"];
    }

    public override Task<ActivityDataContextResponse> HandleAsync(ActivityDataContextRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return HttpContext.RequestServices.GetRequiredService<ActivityDataContextService>().ResolveAsync(HttpContext, request, cancellationToken);
    }
}
