using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Challenge(IAuthenticationProviderResolver providers) : ElsaEndpoint<ProviderRouteRequest>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("challenge/{provider}"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProviderRouteRequest req, CancellationToken ct)
    {
        var descriptor = await providers.FindAsync(req.Provider, allowGlobalFallback: true, cancellationToken: ct);
        if (descriptor?.Challenge is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await HttpContext.ChallengeAsync(
            descriptor.Challenge.Scheme ?? descriptor.Id,
            new AuthenticationProperties { RedirectUri = Query<string>("returnUrl", isRequired: false) ?? "/" });
    }
}
