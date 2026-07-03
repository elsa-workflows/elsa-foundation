using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

/// <summary>
/// Makes the OIDC JWT bearer scheme the default authenticate/challenge scheme when
/// <see cref="OidcAuthenticationOptions.IsDefault"/> is set and the host has not chosen its own
/// defaults. Without a default challenge scheme, an unauthenticated request to an authorized
/// endpoint throws instead of producing a 401 challenge.
/// </summary>
public sealed class ConfigureOidcDefaultAuthenticationSchemes(IOptions<OidcAuthenticationOptions> options) :
    IConfigureOptions<AuthenticationOptions>
{
    public void Configure(AuthenticationOptions target)
    {
        var value = options.Value;

        if (!value.IsDefault || target.DefaultScheme is not null)
            return;

        target.DefaultAuthenticateScheme ??= value.JwtBearerScheme;
        target.DefaultChallengeScheme ??= value.JwtBearerScheme;
    }
}
