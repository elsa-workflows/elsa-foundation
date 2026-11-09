using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Endpoints.ExpressionTooling.Complete;

[Post("/design/activities/expression-tooling/completions")]
[RequirePermission(ActivityDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ActivityExpressionToolingRequest, ActivityExpressionToolingResponse<ExpressionToolingItems>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivitiesExpressionToolingComplete";
        options.Accepts = ["application/json"];
    }

    public override async Task<ActivityExpressionToolingResponse<ExpressionToolingItems>> HandleAsync(ActivityExpressionToolingRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var service = HttpContext.RequestServices.GetRequiredService<ActivityExpressionToolingOperations>();
        return new(await service.InvokeAsync<ExpressionToolingItems>(request,
            (operations, location, authorization, token) => operations.CompleteAsync(location, request.Source, request.Cursor, authorization, token), cancellationToken));
    }
}
