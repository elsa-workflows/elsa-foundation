using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictBehaviorServiceCollectionExtensions
{
    /// <summary>
    /// Registers provider-neutral OpenIddict server, validation, authentication-selection, and token behavior.
    /// A persistence adapter must separately configure the OpenIddict Core stores.
    /// </summary>
    public static IServiceCollection AddFoundationIdentityOpenIddictBehavior(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<OpenIddictIdentityOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, OpenIddictAuthenticationProviderModule>());
        services.TryAddScoped<ITokenService, OpenIddictTokenService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIddictServerOptions>, ConfigureOpenIddictServerOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<AuthenticationOptions>, ConfigureOpenIddictDefaultAuthenticationSchemes>());

        services.AddOpenIddict()
            .AddServer(server => server.AllowCustomFlow(OpenIddictIdentityDefaults.FirstPartyGrantType))
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.EnableTokenEntryValidation();
                validation.UseAspNetCore();
            });

        services.AddAuthentication().AddPolicyScheme(
            OpenIddictIdentityDefaults.SelectorScheme,
            "Elsa Identity scheme selector",
            policy => policy.ForwardDefaultSelector = OpenIddictSchemeSelector.SelectScheme);

        var snapshot = new OpenIddictIdentityOptions();
        configure?.Invoke(snapshot);
        if (snapshot.IsDevelopmentOrDemo)
            services.AddIdentityDevelopmentOrDemoGuard("FoundationIdentityOpenIddict");

        return services;
    }
}
