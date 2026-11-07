using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Keeps optional expression operations behind the activity owner's authorization and location.</summary>
internal sealed class ActivityExpressionToolingOperations(
    IEnumerable<IExpressionToolingOperations> operations,
    IEnumerable<IExpressionAuthoringAuthorizationPolicy> policies,
    IActivityAuthoringContextAsync actor,
    IHttpContextAccessor httpContext)
{
    public async ValueTask<ExpressionToolingOutcome<T>> InvokeAsync<T>(
        ActivityExpressionToolingRequest request,
        Func<IExpressionToolingOperations, ResolveExpressionAuthoringContextRequest, ExpressionAuthoringAuthorization, CancellationToken, ValueTask<ExpressionToolingOutcome<T>>> invoke,
        CancellationToken cancellationToken)
    {
        var available = operations.Take(2).ToArray();
        var authorizationPolicies = policies.Take(2).ToArray();
        if (available.Length != 1 || authorizationPolicies.Length != 1)
            return ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1, "activity-expression-tooling-unavailable");
        var profile = await actor.GetAuthorizationProfileAsync(cancellationToken);
        // Token-instance claims rotate per request; every other claim participates in permission invalidation.
        var claims = (httpContext.HttpContext?.User.Claims ?? [])
            .Where(claim => claim.Type is not ("jti" or "iat" or "nbf" or "exp" or "oi_tkn_id" or "oi_crt_dt" or "oi_exp_dt"))
            .Select(claim => $"{claim.Type}\u001f{claim.Value}").OrderBy(value => value, StringComparer.Ordinal);
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile + "\u001e" + string.Join('\u001e', claims))).AsSpan(0, 16)).ToLowerInvariant();
        var authorization = await authorizationPolicies[0].AuthorizeAsync(new(true, actor.ActorId, revision, revision), cancellationToken);
        return await invoke(available[0], request.ToLocation(), authorization, cancellationToken);
    }
}
