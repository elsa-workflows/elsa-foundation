using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

internal sealed class ConfigureEfCoreIdentityDefaultAuthenticationSchemes : IConfigureOptions<AuthenticationOptions>
{
    public void Configure(AuthenticationOptions options)
    {
        if (options.DefaultScheme is not null)
            return;
        options.DefaultAuthenticateScheme ??= AspNetCoreIdentityDefaults.CookieScheme;
        options.DefaultSignInScheme ??= AspNetCoreIdentityDefaults.CookieScheme;
    }
}
