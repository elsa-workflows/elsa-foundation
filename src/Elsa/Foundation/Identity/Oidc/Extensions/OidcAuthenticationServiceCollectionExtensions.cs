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

        var authentication = services.AddAuthentication()
            .AddJwtBearer(options.JwtBearerScheme, _ => { });

        // The interactive OpenID Connect handler is a remote-authentication request handler that
        // eagerly validates its options (ClientId is required) for EVERY request, regardless of the
        // default challenge scheme. Registering it while unconfigured therefore faults every request
        // with a 500 before authorization can produce the intended 401. It is only usable once a
        // provider is configured (ClientId + Authority), so we register it only when a ClientId is
        // present. Without it, bearer-token validation (JwtBearer) remains the default challenge and
        // unauthenticated API calls return 401 as designed. Binding a real provider from shell
        // configuration (so ClientId/Authority arrive without an explicit configure delegate) is
        // W18 scope, not W4.
        if (!string.IsNullOrWhiteSpace(options.ClientId))
            authentication.AddOpenIdConnect(options.AuthenticationScheme, _ => { });

        return services;
    }
}
