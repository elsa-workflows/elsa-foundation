using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Refresh(ITokenService tokenService) : ElsaEndpoint<RefreshTokenRequest, TokenRefreshResult>
{
    public override void Configure()
    {
        Post(IdentityRouteConstants.GetRoute("refresh"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        var result = await tokenService.RefreshAsync(new TokenRefreshRequest(req.RefreshToken), ct);
        await Send.OkAsync(result, ct);
    }
}
