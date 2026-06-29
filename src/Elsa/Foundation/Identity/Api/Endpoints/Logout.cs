using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Logout : ElsaEndpoint<ProviderRouteRequest>
{
    public override void Configure()
    {
        Post(IdentityRouteConstants.GetRoute("logout/{provider}"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProviderRouteRequest req, CancellationToken ct)
    {
        await HttpContext.SignOutAsync(req.Provider);
        await Send.NoContentAsync(ct);
    }
}
