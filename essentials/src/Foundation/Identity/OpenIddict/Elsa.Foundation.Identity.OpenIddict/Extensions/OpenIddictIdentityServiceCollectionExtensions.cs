using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictIdentityServiceCollectionExtensions
{
    /// <summary>Composes provider-neutral OpenIddict behavior without selecting a persistence provider.</summary>
    public static IServiceCollection AddFoundationIdentityOpenIddict(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null) =>
        services.AddFoundationIdentityOpenIddictBehavior(configure);
}
