using System.Security.Claims;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Api.Endpoints;

/// <summary>
/// <c>GET /_elsa/identity/token</c> — the first-party cookie→bearer exchange (plan Workstream C). Runs under
/// the interactive session schemes (cookie / external OIDC), NOT the first-party bearer validation scheme
/// (that would be circular), and mints a bearer for the already-authenticated principal via
/// <see cref="ITokenService"/>. Anonymous callers get 401 — the Studio client treats 401 as "no token" and
/// stays anonymous — so this endpoint deliberately does not <c>AllowAnonymous</c>.
/// </summary>
internal sealed class Token(
    ITokenService tokens,
    IOptions<FoundationIdentityApiOptions> options,
    IAuthenticationSchemeProvider schemes) : ElsaEndpointWithoutRequest<AccessTokenResponse>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("token"));

        // Authenticate under the interactive schemes only. Drop names that aren't registered in this host so
        // the endpoint composes with whatever session modules are enabled (and so a scheme the app never
        // registered can't fault authentication). Explicitly listing them means the endpoint works even when
        // the composite scheme selector is not the host's default scheme.
        var interactiveSchemes = options.Value.InteractiveAuthSchemes
            .Where(name => schemes.GetSchemeAsync(name).GetAwaiter().GetResult() is not null)
            .ToArray();

        if (interactiveSchemes.Length > 0)
            AuthSchemes(interactiveSchemes);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var tenantId = User.FindFirstValue(IdentityClaimTypes.TenantId) ?? "default";
        var permissions = User.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToArray();

        var result = await tokens.IssueAsync(new TokenIssueRequest(subject, tenantId, permissions), ct);
        await Send.OkAsync(new AccessTokenResponse(result.AccessToken, result.ExpiresAt), ct);
    }
}
