using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Logout(IAuthenticationProviderResolver providers) : ElsaEndpoint<ProviderRouteRequest>
{
    public override void Configure()
    {
        Post(IdentityRouteConstants.GetRoute("logout/{provider}"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProviderRouteRequest req, CancellationToken ct)
    {
        // Sign out on the provider's own authentication scheme when it declares one (e.g. the first-party
        // Identity provider clears its cookie scheme), falling back to the provider id itself.
        var descriptor = await providers.FindAsync(req.Provider, allowGlobalFallback: true, cancellationToken: ct);
        var scheme = descriptor?.Challenge?.Scheme ?? req.Provider;

        await HttpContext.SignOutAsync(scheme);
        await Send.NoContentAsync(ct);
    }
}
