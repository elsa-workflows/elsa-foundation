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

        // IsDefault is a single-winner signal: with only one OIDC provider composed per shell it
        // maps cleanly to the shell's default challenge scheme. Multiple providers each marking
        // themselves default is effectively last-wins today; coherent multi-provider default-scheme
        // selection (precedence, explicit host override arbitration) is W18 scope, not W4.
        if (!value.IsDefault || target.DefaultScheme is not null)
            return;

        target.DefaultAuthenticateScheme ??= value.JwtBearerScheme;
        target.DefaultChallengeScheme ??= value.JwtBearerScheme;
    }
}
