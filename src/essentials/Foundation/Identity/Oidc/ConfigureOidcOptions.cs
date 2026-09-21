using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

internal sealed class ConfigureOidcOptions(IOptions<OidcAuthenticationOptions> options) :
    IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(string? name, OpenIdConnectOptions target)
    {
        if (!string.Equals(name, options.Value.AuthenticationScheme, StringComparison.Ordinal))
            return;

        target.Authority = options.Value.Authority;
        target.ClientId = options.Value.ClientId;
        target.ClientSecret = options.Value.ClientSecret;
        target.RequireHttpsMetadata = options.Value.RequireHttpsMetadata;
        target.ResponseType = "code";
        target.SaveTokens = true;
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}

internal sealed class ConfigureOidcJwtBearerOptions(IOptions<OidcAuthenticationOptions> options) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions target)
    {
        if (!string.Equals(name, options.Value.JwtBearerScheme, StringComparison.Ordinal))
            return;

        target.Authority = options.Value.Authority;
        target.Audience = options.Value.ClientId;
        target.RequireHttpsMetadata = options.Value.RequireHttpsMetadata;
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
