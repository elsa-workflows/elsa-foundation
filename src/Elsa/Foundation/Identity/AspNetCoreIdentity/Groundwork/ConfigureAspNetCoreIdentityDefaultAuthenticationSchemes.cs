using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;

/// <summary>
/// Makes the first-party Identity cookie the default authenticate/sign-in scheme for Groundwork-only Identity
/// hosts. The shared ASP.NET Core Identity core extension intentionally does not install this globally, because
/// OpenIddict/API compositions own their own challenge and bearer-selection behavior.
/// </summary>
internal sealed class ConfigureAspNetCoreIdentityDefaultAuthenticationSchemes :
    IConfigureOptions<AuthenticationOptions>
{
    public void Configure(AuthenticationOptions target)
    {
        if (target.DefaultScheme is not null)
            return;

        target.DefaultAuthenticateScheme ??= AspNetCoreIdentityDefaults.CookieScheme;
        target.DefaultSignInScheme ??= AspNetCoreIdentityDefaults.CookieScheme;
    }
}
