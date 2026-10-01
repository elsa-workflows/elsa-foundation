using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Makes the scheme selector (<see cref="OpenIddictIdentityDefaults.SelectorScheme"/>) the default
/// authenticate/challenge scheme so bearer tokens issued by this module authenticate API calls out of the
/// box, while still routing external JWTs and cookie sessions to their own handlers.
/// </summary>
/// <remarks>
/// Composition with the OIDC module: its <c>ConfigureOidcDefaultAuthenticationSchemes</c> sets the external
/// JwtBearer scheme as default (with <c>??=</c>). Since our selector forwards external bearer tokens to that
/// same scheme, we take over not only when no default is set but also when the current default IS that
/// external scheme — the outcome is then identical for external tokens and deterministic regardless of
/// feature registration order. The same holds for the interactive cookie scheme: the ASP.NET Core Identity
/// module defaults authentication to its cookie, and the selector already routes cookie requests back to it,
/// so taking over keeps cookie sessions working while letting bearer tokens authenticate the ambient user.
/// A host-chosen <c>DefaultScheme</c> is always respected.
/// </remarks>
internal sealed class ConfigureOpenIddictDefaultAuthenticationSchemes(IOptions<OpenIddictIdentityOptions> options) :
    IConfigureOptions<AuthenticationOptions>
{
    public void Configure(AuthenticationOptions target)
    {
        var value = options.Value;

        if (!value.Enabled || target.DefaultScheme is not null)
            return;

        if (target.DefaultAuthenticateScheme is null || target.DefaultAuthenticateScheme == value.ExternalBearerScheme
            || target.DefaultAuthenticateScheme == value.InteractiveScheme)
            target.DefaultAuthenticateScheme = OpenIddictIdentityDefaults.SelectorScheme;

        if (target.DefaultChallengeScheme is null || target.DefaultChallengeScheme == value.ExternalBearerScheme)
            target.DefaultChallengeScheme = OpenIddictIdentityDefaults.SelectorScheme;
    }
}
