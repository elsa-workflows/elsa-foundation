using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Endpoints.ExpressionTooling.ResolveContext;

[Post("/design/activities/expression-tooling/context")]
[RequirePermission(ActivityDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ActivityExpressionToolingRequest, ActivityExpressionToolingResponse<ExpressionAuthoringContext>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivitiesExpressionToolingResolveContext";
        options.Accepts = ["application/json"];
    }

    public override async Task<ActivityExpressionToolingResponse<ExpressionAuthoringContext>> HandleAsync(ActivityExpressionToolingRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var service = HttpContext.RequestServices.GetRequiredService<ActivityExpressionToolingOperations>();
        return new(await service.InvokeAsync<ExpressionAuthoringContext>(request,
            (operations, location, authorization, token) => operations.ResolveContextAsync(location, authorization, token), cancellationToken));
    }
}
