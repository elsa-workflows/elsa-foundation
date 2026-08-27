using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.CompleteExpressionTooling;

[Post("expression-tooling/completions")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ExpressionToolingCompletionRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringCompleteExpressionTooling";
        options.Accepts = ["application/json"];
    }

    public override async Task<ExpressionToolingOperationResponse<ExpressionToolingItems>> HandleAsync(
        ExpressionToolingCompletionRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return new(await ExpressionToolingApiHandlers.CompleteAsync(HttpContext, request));
    }
}
