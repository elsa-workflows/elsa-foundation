using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

internal sealed record AuthorizationMiddlewareResultHandlerFallback(IAuthorizationMiddlewareResultHandler Handler);

internal sealed class PermissionAuthorizationMiddlewareResultHandler(
    AuthorizationMiddlewareResultHandlerFallback fallback,
    NormalizedPrincipalValidator validator) : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            context.User.Identities.Any(identity => identity.IsAuthenticated) &&
            policy.Requirements.Any(requirement => requirement is NormalizedPermissionPrincipalRequirement) &&
            !validator.TryGetNormalizedPrincipal(context.User, out _))
        {
            authorizeResult = PolicyAuthorizationResult.Challenge();
        }

        return fallback.Handler.HandleAsync(next, context, policy, authorizeResult);
    }
}
