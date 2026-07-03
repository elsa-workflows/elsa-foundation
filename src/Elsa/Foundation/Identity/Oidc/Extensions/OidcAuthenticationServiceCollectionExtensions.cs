using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc.Extensions;

public static class OidcAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityOidc(this IServiceCollection services, Action<OidcAuthenticationOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<OidcAuthenticationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, OidcAuthenticationProviderModule>());
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureOidcOptions>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureOidcJwtBearerOptions>();
        services.AddSingleton<IConfigureOptions<AuthenticationOptions>, ConfigureOidcDefaultAuthenticationSchemes>();

        var options = new OidcAuthenticationOptions();
        configure?.Invoke(options);

        services.AddAuthentication()
            .AddOpenIdConnect(options.AuthenticationScheme, _ => { })
            .AddJwtBearer(options.JwtBearerScheme, _ => { });

        return services;
    }
}
