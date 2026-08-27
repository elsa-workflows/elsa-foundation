using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.HoverExpressionTooling;

[Post("expression-tooling/hover")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ExpressionToolingHoverRequest, ExpressionToolingOperationResponse<ExpressionHover>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringHoverExpressionTooling";
        options.Accepts = ["application/json"];
    }

    public override async Task<ExpressionToolingOperationResponse<ExpressionHover>> HandleAsync(
        ExpressionToolingHoverRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return new(await ExpressionToolingApiHandlers.HoverAsync(HttpContext, request));
    }
}
