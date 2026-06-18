using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityOpenIddict(this IServiceCollection services, Action<OpenIddictIdentityOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<OpenIddictIdentityOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, OpenIddictAuthenticationProviderModule>());
        services.TryAddSingleton<ITokenService, OpenIddictTokenService>();

        return services;
    }
}
