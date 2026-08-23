using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.ValidateExpressionTooling;

[Post("expression-tooling/validate")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : ApiEndpoint<ExpressionToolingSourceRequest, ExpressionToolingOperationResponse<ExpressionDiagnosticSet>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringValidateExpressionTooling";
        options.Accepts = ["application/json"];
    }

    public override async Task<ExpressionToolingOperationResponse<ExpressionDiagnosticSet>> HandleAsync(
        ExpressionToolingSourceRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return new(await ExpressionToolingApiHandlers.ValidateAsync(HttpContext, request));
    }
}
