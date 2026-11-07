using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Design.Api;

internal static class ExpressionToolingApiHandlers
{
    public static ValueTask<ExpressionAuthoringAuthorization> AuthorizationAsync(
        HttpContext context,
        IExpressionAuthoringAuthorizationPolicy policy,
        CancellationToken cancellationToken)
    {
        var subjectId = context.User.FindFirstValue("sub") ?? context.User.Identity?.Name;
        // Token-instance identifiers/timestamps rotate on Studio's per-request bearer exchange, not on
        // permission changes. Authentication still validates each token's lifetime and revocation before
        // these handlers run. Keep every other claim (including private scopes/audiences/presenters) so
        // subject, tenant, grants and host-policy changes continue to invalidate authoring contexts.
        var permissionRevision = Revision(context.User.Claims
            .Where(claim => claim.Type is not ("jti" or "iat" or "nbf" or "exp" or "oi_tkn_id" or "oi_crt_dt" or "oi_exp_dt"))
            .Select(claim => $"{claim.Type}\u001f{claim.Value}")
            .OrderBy(value => value, StringComparer.Ordinal));
        return policy.AuthorizeAsync(new(true, subjectId, permissionRevision, permissionRevision), cancellationToken);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveContextAsync(HttpContext context, ExpressionToolingContextRequest request)
    {
        var operations = context.RequestServices.GetServices<IExpressionToolingOperations>().Take(2).ToArray();
        if (operations.Length != 1)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "expression-tooling-unavailable");
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var location = new ResolveExpressionAuthoringContextRequest(request.ContractVersion, request.WorkflowDraftId, request.NodeId,
            request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision, request.Search, request.Skip, request.Take);
        return await operations[0].ResolveContextAsync(location, authorization, context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> SearchSymbolsAsync(HttpContext context, ExpressionToolingContextRequest request)
    {
        var operations = context.RequestServices.GetServices<IExpressionToolingOperations>().Take(2).ToArray();
        if (operations.Length != 1)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "expression-tooling-unavailable");
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var location = new ResolveExpressionAuthoringContextRequest(request.ContractVersion, request.WorkflowDraftId, request.NodeId,
            request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision, request.Search, request.Skip, request.Take);
        return await operations[0].SearchSymbolsAsync(location, authorization, context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> CompleteAsync(HttpContext context, ExpressionToolingCompletionRequest request)
    {
        var operations = context.RequestServices.GetServices<IExpressionToolingOperations>().Take(2).ToArray();
        if (operations.Length != 1)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "expression-tooling-unavailable");
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var location = new ResolveExpressionAuthoringContextRequest(request.ContractVersion, request.WorkflowDraftId, request.NodeId,
            request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision);
        return await operations[0].CompleteAsync(location, request.Source, request.Cursor, authorization, context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionHover>> HoverAsync(HttpContext context, ExpressionToolingHoverRequest request)
    {
        var operations = context.RequestServices.GetServices<IExpressionToolingOperations>().Take(2).ToArray();
        if (operations.Length != 1)
            return ExpressionToolingOutcome<ExpressionHover>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "expression-tooling-unavailable");
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var location = new ResolveExpressionAuthoringContextRequest(request.ContractVersion, request.WorkflowDraftId, request.NodeId,
            request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision);
        return await operations[0].HoverAsync(location, request.Source, request.Position, authorization, context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(HttpContext context, ExpressionToolingSourceRequest request)
    {
        var operations = context.RequestServices.GetServices<IExpressionToolingOperations>().Take(2).ToArray();
        if (operations.Length != 1)
            return ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "expression-tooling-unavailable");
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var location = new ResolveExpressionAuthoringContextRequest(request.ContractVersion, request.WorkflowDraftId, request.NodeId,
            request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision);
        return await operations[0].ValidateAsync(location, request.Source, authorization, context.RequestAborted);
    }

    private static string Revision(IEnumerable<string> values)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', values)));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }
}
