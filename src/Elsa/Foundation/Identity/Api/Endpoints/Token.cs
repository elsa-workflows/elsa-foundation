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
/// <see cref="ITokenService"/>. Anonymous callers get a clean 401 — the Studio client treats 401 as "no
/// token" and stays anonymous — and are never able to obtain a token (see <see cref="HandleAsync"/>).
/// </summary>
internal sealed class Token(
    ITokenService tokens,
    IOptions<FoundationIdentityApiOptions> options,
    IAuthenticationSchemeProvider schemes,
    NormalizedPrincipalValidator principalValidator) : ElsaEndpointWithoutRequest<AccessTokenResponse>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("token"));

        // Authenticate under the interactive schemes only — the first-party cookie and the external-OIDC
        // JwtBearer — never the first-party bearer validation scheme (that would be circular). Drop names
        // that aren't registered in this host so the endpoint composes with whatever session modules are
        // enabled. Naming the schemes explicitly means the cookie/OIDC principal is read into `User` even
        // when the composite scheme selector is not the host's default scheme.
        var interactiveSchemes = options.Value.InteractiveAuthSchemes
            .Where(name => schemes.GetSchemeAsync(name).GetAwaiter().GetResult() is not null)
            .ToArray();

        if (interactiveSchemes.Length > 0)
            AuthSchemes(interactiveSchemes);

        // Authenticate (above) but do not gate authorization: an anonymous caller must reach the handler and
        // receive a clean 401 rather than a scheme challenge (the cookie scheme's challenge is a 302 redirect
        // to the login page, which the Studio client — expecting a bare 401 to mean "no token" — cannot
        // consume). The handler itself refuses to issue a token to an unauthenticated principal, so the
        // endpoint is never actually anonymous-accessible.
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        // Only provider-projected identities may cross the interactive-session -> first-party bearer trust
        // boundary. In particular, never copy internal permission claims directly from an external JWT.
        if (!principalValidator.TryGetNormalizedPrincipal(User, out var normalizedPrincipal))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var subject = normalizedPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? normalizedPrincipal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var tenantId = normalizedPrincipal.FindFirstValue(IdentityClaimTypes.TenantId) ?? "default";
        var permissions = normalizedPrincipal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToArray();

        var result = await tokens.IssueAsync(new TokenIssueRequest(subject, tenantId, permissions), ct);
        await Send.OkAsync(new AccessTokenResponse(result.AccessToken, result.ExpiresAt), ct);
    }
}
