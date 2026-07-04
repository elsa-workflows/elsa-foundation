using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Validation.AspNetCore;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Picks the concrete authentication scheme for a request behind the
/// <see cref="OpenIddictIdentityDefaults.SelectorScheme"/> policy scheme:
/// <list type="bullet">
/// <item><c>Authorization: Bearer</c> with a first-party token (issuer matches
/// <see cref="OpenIddictIdentityOptions.Issuer"/>, or unparseable) → the OpenIddict validation handler.</item>
/// <item><c>Authorization: Bearer</c> with a foreign issuer → <see cref="OpenIddictIdentityOptions.ExternalBearerScheme"/>
/// (the external-OIDC JwtBearer scheme), when that scheme is registered.</item>
/// <item>No bearer but the identity cookie present → <see cref="OpenIddictIdentityOptions.InteractiveScheme"/>,
/// when registered.</item>
/// <item>Anything else → the OpenIddict validation handler, whose challenge is a clean 401.</item>
/// </list>
/// Forward targets from other modules are referenced by name only and verified against the scheme provider
/// before use, so this module composes with — but never depends on — the OIDC and cookie modules.
/// </summary>
public static class OpenIddictSchemeSelector
{
    private const string BearerPrefix = "Bearer ";

    public static string SelectScheme(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value;

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[BearerPrefix.Length..].Trim();
            if (!IsLocallyIssued(token, options.Issuer) && IsRegistered(context, options.ExternalBearerScheme))
                return options.ExternalBearerScheme;

            return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        }

        if (context.Request.Cookies.ContainsKey(options.InteractiveCookieName) && IsRegistered(context, options.InteractiveScheme))
            return options.InteractiveScheme;

        return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    }

    private static bool IsLocallyIssued(string token, string issuer)
    {
        try
        {
            var jwt = new JsonWebToken(token);
            return string.Equals(jwt.Issuer, new Uri(issuer, UriKind.Absolute).AbsoluteUri, StringComparison.Ordinal);
        }
        catch
        {
            // Not parseable as a JWT (opaque/garbage) — let the local handler produce the 401.
            return true;
        }
    }

    private static bool IsRegistered(HttpContext context, string scheme)
    {
        // The default scheme provider is an in-memory lookup that completes synchronously.
        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        return schemes.GetSchemeAsync(scheme).GetAwaiter().GetResult() is not null;
    }
}
