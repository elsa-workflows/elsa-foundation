using System.Security.Claims;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// The default <see cref="IHttpEndpointAuthorizationHandler"/>: an inbound request is authorized when the caller
/// is authenticated (and, when the endpoint declares a <see cref="AuthorizeHttpEndpointContext.Policy"/>, when
/// that policy succeeds against the authenticated principal). An anonymous or policy-failing caller is denied
/// (returns false → 401); a host misconfiguration that prevents evaluation is surfaced distinctly as an
/// <see cref="HttpEndpointAuthorizationConfigurationException"/> (→ 500), not silently swallowed into a 401
/// (#592 item 11).
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
/// The handler does not mutate <see cref="HttpContext.User"/> (#592 item 11): evaluating the authorize predicate
/// must not rewrite the request principal seen by downstream middleware and activities. A freshly authenticated
/// principal is used locally for the policy check only.
/// </para>
/// <para>
/// The middleware authorizes an inbound request before any workflow instance exists, so there is no protected
/// workflow resource to hand the policy (see <see cref="AuthorizeHttpEndpointContext"/> remarks); the policy is
/// evaluated against the authenticated user alone via <see cref="IAuthorizationService"/>.
/// </para>
/// </remarks>
public sealed class AuthenticationBasedHttpEndpointAuthorizationHandler(
    IAuthorizationService authorizationService,
    ILogger<AuthenticationBasedHttpEndpointAuthorizationHandler>? logger = null) : IHttpEndpointAuthorizationHandler
{
    private readonly ILogger _logger = logger ?? NullLogger<AuthenticationBasedHttpEndpointAuthorizationHandler>.Instance;

    /// <inheritdoc />
    public async ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
    {
        var user = await ResolveAuthenticatedUserAsync(context.HttpContext);

        // Not authenticated (upstream middleware left an anonymous principal and explicit
        // authentication produced none): fail closed with a plain denial (→ 401).
        if (user is null)
            return false;

        if (string.IsNullOrWhiteSpace(context.Policy))
            return true;

        AuthorizationResult authorizationResult;
        try
        {
            authorizationResult = await authorizationService.AuthorizeAsync(user, context.Policy!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A missing/misconfigured policy (e.g. the named policy is not registered) is an operator error,
            // not an unauthorized caller: surface it distinctly so the middleware returns 500, not 401.
            _logger.LogError(exception, "Authorization policy {Policy} could not be evaluated for an HTTP endpoint request", context.Policy);
            throw new HttpEndpointAuthorizationConfigurationException(
                $"The authorization policy '{context.Policy}' could not be evaluated. Verify the policy is registered for the shell.",
                exception);
        }

        return authorizationResult.Succeeded;
    }

    /// <summary>
    /// Returns the authenticated principal for the request, or <c>null</c> when the caller is anonymous. Prefers
    /// the principal already on <see cref="HttpContext.User"/> (populated when authentication middleware ran
    /// ahead of this handler); otherwise authenticates explicitly against the shell's default scheme so the
    /// handler does not depend on middleware ordering. Does not write the result back to
    /// <see cref="HttpContext.User"/> (#592 item 11).
    /// </summary>
    private async ValueTask<ClaimsPrincipal?> ResolveAuthenticatedUserAsync(HttpContext httpContext)
    {
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true)
            return user;

        AuthenticateResult result;
        try
        {
            result = await httpContext.AuthenticateAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No default authentication scheme registered for the shell (or the scheme threw): an operator
            // configuration error, distinct from an anonymous caller. Surface it as 500 rather than 401.
            _logger.LogError(exception, "Authentication could not be evaluated for an HTTP endpoint request (no scheme or scheme faulted)");
            throw new HttpEndpointAuthorizationConfigurationException(
                "Authentication could not be evaluated. Verify an authentication scheme is registered for the shell.",
                exception);
        }

        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
            return null;

        return result.Principal;
    }
}
