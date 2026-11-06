using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Endpoints.ExpressionTooling.Hover;

[Post("/design/activities/expression-tooling/hover")]
[RequirePermission(ActivityDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ActivityExpressionToolingRequest, ActivityExpressionToolingResponse<ExpressionHover>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivitiesExpressionToolingHover";
        options.Accepts = ["application/json"];
    }

    public override async Task<ActivityExpressionToolingResponse<ExpressionHover>> HandleAsync(ActivityExpressionToolingRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var service = HttpContext.RequestServices.GetRequiredService<ActivityExpressionToolingOperations>();
        return new(await service.InvokeAsync<ExpressionHover>(request,
            (operations, location, authorization, token) => operations.HoverAsync(location, request.Source, request.Position, authorization, token), cancellationToken));
    }
}
