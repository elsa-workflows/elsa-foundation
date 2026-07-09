using System.Security.Claims;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// The default <see cref="IHttpEndpointAuthorizationHandler"/>: an inbound request is authorized when the caller
/// is authenticated (and, when the endpoint declares a <see cref="AuthorizeHttpEndpointContext.Policy"/>, when
/// that policy succeeds against the authenticated principal). Fails closed on any inability to authenticate or
/// evaluate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-shell authentication (spec 089 sub-unit C, T008).</b> The endpoint middleware runs inside the shell's
/// request branch. Whether <see cref="HttpContext.User"/> is already populated depends on host wiring: in the
/// default composition the root <c>UseAuthentication</c> middleware runs after the shell-resolution middleware
/// (which swaps <c>RequestServices</c> to the shell scope) and therefore authenticates against the shell's own
/// schemes before this handler is reached. Rather than assume that ordering, the handler authenticates
/// explicitly: if <see cref="HttpContext.User"/> is not already authenticated it calls
/// <see cref="AuthenticationHttpContextExtensions.AuthenticateAsync(HttpContext)"/> against the shell's default
/// scheme (resolved from <c>HttpContext.RequestServices</c>, i.e. the shell scope, so the shell's configured
/// authentication stack is honored). A populated principal from upstream middleware is used as-is; an explicit
/// authenticate that does not yield an authenticated principal denies the request.
/// </para>
/// <para>
/// The middleware authorizes an inbound request before any workflow instance exists, so there is no protected
/// workflow resource to hand the policy (see <see cref="AuthorizeHttpEndpointContext"/> remarks); the policy is
/// evaluated against the authenticated user alone via <see cref="IAuthorizationService"/>.
/// </para>
/// </remarks>
public sealed class AuthenticationBasedHttpEndpointAuthorizationHandler(IAuthorizationService authorizationService) : IHttpEndpointAuthorizationHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
    {
        var httpContext = context.HttpContext;

        try
        {
            var user = await ResolveAuthenticatedUserAsync(httpContext);

            // Not authenticated (upstream middleware left an anonymous principal and explicit
            // authentication produced none): fail closed.
            if (user is null)
                return false;

            if (string.IsNullOrWhiteSpace(context.Policy))
                return true;

            var authorizationResult = await authorizationService.AuthorizeAsync(user, context.Policy!);

            return authorizationResult.Succeeded;
        }
        catch
        {
            // Any inability to authenticate or evaluate the policy (missing scheme, handler throwing, …) is
            // treated as a denial: the endpoint stays protected.
            return false;
        }
    }

    /// <summary>
    /// Returns the authenticated principal for the request, or <c>null</c> when the caller is anonymous. Prefers
    /// the principal already on <see cref="HttpContext.User"/> (populated when authentication middleware ran
    /// ahead of this handler); otherwise authenticates explicitly against the shell's default scheme so the
    /// handler does not depend on middleware ordering.
    /// </summary>
    private static async ValueTask<ClaimsPrincipal?> ResolveAuthenticatedUserAsync(HttpContext httpContext)
    {
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true)
            return user;

        var result = await httpContext.AuthenticateAsync();

        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
            return null;

        // Make the freshly authenticated principal visible to the policy evaluation below and to the rest of the
        // pipeline (downstream middleware / activities read HttpContext.User).
        httpContext.User = result.Principal;
        return result.Principal;
    }
}
