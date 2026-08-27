using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.ResolveExpressionToolingContext;

[Post("expression-tooling/context")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ExpressionToolingContextRequest, ExpressionToolingContextResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringResolveExpressionToolingContext";
        options.Accepts = ["application/json"];
    }

    public override async Task<ExpressionToolingContextResponse> HandleAsync(ExpressionToolingContextRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return new(await ExpressionToolingApiHandlers.ResolveContextAsync(HttpContext, request));
    }
}
