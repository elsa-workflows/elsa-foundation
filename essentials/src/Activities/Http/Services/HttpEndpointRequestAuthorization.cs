using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>Shared fail-closed enforcement of the authorization policy attached to an HTTP endpoint binding.</summary>
internal static class HttpEndpointRequestAuthorization
{
    public static async ValueTask<bool> AuthorizeAsync(
        IHttpEndpointAuthorizationHandler handler,
        HttpContext context,
        IReadOnlyCollection<string> policies)
    {
        if (policies.Count == 0)
            return await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, Policy: null));

        foreach (var policy in policies)
            if (!await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, policy)))
                return false;

        return true;
    }
}
