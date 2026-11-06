using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Endpoints.ExpressionTooling.Validate;

[Post("/design/activities/expression-tooling/validate")]
[RequirePermission(ActivityDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ActivityExpressionToolingRequest, ActivityExpressionToolingResponse<ExpressionDiagnosticSet>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ActivitiesExpressionToolingValidate";
        options.Accepts = ["application/json"];
    }

    public override async Task<ActivityExpressionToolingResponse<ExpressionDiagnosticSet>> HandleAsync(ActivityExpressionToolingRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var service = HttpContext.RequestServices.GetRequiredService<ActivityExpressionToolingOperations>();
        return new(await service.InvokeAsync<ExpressionDiagnosticSet>(request,
            (operations, location, authorization, token) => operations.ValidateAsync(location, request.Source, authorization, token), cancellationToken));
    }
}
