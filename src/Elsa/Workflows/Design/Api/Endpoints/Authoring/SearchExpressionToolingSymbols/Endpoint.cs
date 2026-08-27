using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.SearchExpressionToolingSymbols;

[Post("expression-tooling/symbols")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ExpressionToolingContextRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringSearchExpressionToolingSymbols";
        options.Accepts = ["application/json"];
    }

    public override async Task<ExpressionToolingOperationResponse<ExpressionToolingItems>> HandleAsync(
        ExpressionToolingContextRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return new(await ExpressionToolingApiHandlers.SearchSymbolsAsync(HttpContext, request));
    }
}
