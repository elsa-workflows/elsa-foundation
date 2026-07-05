using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;

namespace Elsa.Foundation.Identity.Api.Endpoints;

/// <summary>
/// <c>POST /_elsa/identity/refresh</c> — redeems a rotating refresh token for a fresh access/refresh pair.
/// An invalid, expired, revoked, or replayed refresh token yields a clean 401 (matching <c>Token.cs</c>'s
/// "no valid credential" contract), never a 500: <see cref="ITokenService.RefreshAsync"/> signals those cases
/// by throwing <see cref="InvalidOperationException"/>, which this endpoint maps to Unauthorized.
/// </summary>
internal sealed class Refresh(ITokenService tokenService) : ElsaEndpoint<RefreshTokenRequest, TokenRefreshResult>
{
    public override void Configure()
    {
        Post(IdentityRouteConstants.GetRoute("refresh"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        TokenRefreshResult result;
        try
        {
            result = await tokenService.RefreshAsync(new TokenRefreshRequest(req.RefreshToken), ct);
        }
        catch (InvalidOperationException)
        {
            // Invalid / expired / revoked / replayed refresh token — return 401, not 500.
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(result, ct);
    }
}
