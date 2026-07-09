using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Authorization;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Initializes a new instance of the <see cref="AuthenticationBasedHttpEndpointAuthorizationHandler"/> class.
/// </summary>
internal sealed class AuthenticationBasedHttpEndpointAuthorizationHandler(IAuthorizationService authorizationService) : IHttpEndpointAuthorizationHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;
        var identity = user.Identity;

        if (identity == null)
            return false;

        if (identity.IsAuthenticated == false)
            return false;

        if (string.IsNullOrWhiteSpace(context.Policy))
            return identity.IsAuthenticated;

        // The middleware authorizes an inbound request before any workflow instance exists, so there is no
        // protected workflow resource to hand the policy (see AuthorizeHttpEndpointContext remarks); the policy
        // is evaluated against the authenticated user alone.
        var authorizationResult = await authorizationService.AuthorizeAsync(user, context.Policy!);

        return authorizationResult.Succeeded;
    }
}