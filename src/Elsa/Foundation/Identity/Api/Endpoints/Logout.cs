using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.Api.Endpoints;

/// <summary>
/// <c>POST /_elsa/identity/logout/{provider}</c> — idempotent sign-out. Resolves the target scheme from the
/// provider's declared challenge scheme (falling back to the provider id), then signs out only when that
/// scheme is registered and its handler is an <see cref="IAuthenticationSignOutHandler"/>. Anything else — an
/// unknown provider, or a provider (like OpenIddict) whose handler cannot sign out — is a no-op 204 rather
/// than a 500, so an anonymous or bad-provider logout never faults.
/// </summary>
internal sealed class Logout(
    IAuthenticationProviderResolver providers,
    IEnumerable<IAuthenticationSessionInvalidator> sessionInvalidators,
    IAuthenticationSchemeProvider schemeProvider,
    IAuthenticationHandlerProvider handlerProvider) : ElsaEndpoint<ProviderRouteRequest>
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
        var schemeName = descriptor?.Challenge?.Scheme ?? req.Provider;

        // A provider may own server-side state that survives deletion of the client cookie. Invalidate that
        // state first so a failure is truthful: the endpoint must not return 204 or clear the browser copy
        // while a replayable server session remains valid. Providers without such state register no handler.
        var sessionInvalidator = descriptor is null
            ? null
            : sessionInvalidators.SingleOrDefault(candidate =>
                string.Equals(candidate.ProviderId, descriptor.Id, StringComparison.OrdinalIgnoreCase));
        if (sessionInvalidator is not null)
            await sessionInvalidator.InvalidateAsync(new AuthenticationSessionInvalidationContext(User), ct);

        // Only sign out when the scheme is actually registered AND its handler can sign out. OpenIddict's
        // provider declares no challenge scheme, so schemeName falls back to an unregistered id — calling
        // SignOutAsync on it would throw InvalidOperationException and surface a 500 from this anonymous
        // endpoint. Resolving the scheme + confirming IAuthenticationSignOutHandler keeps logout idempotent.
        if (!string.IsNullOrEmpty(schemeName) && await schemeProvider.GetSchemeAsync(schemeName) is not null)
        {
            var handler = await handlerProvider.GetHandlerAsync(HttpContext, schemeName);
            if (handler is IAuthenticationSignOutHandler)
                await HttpContext.SignOutAsync(schemeName);
        }

        await Send.NoContentAsync(ct);
    }
}
