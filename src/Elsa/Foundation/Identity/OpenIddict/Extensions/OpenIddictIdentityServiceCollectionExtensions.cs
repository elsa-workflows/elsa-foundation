using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictIdentityServiceCollectionExtensions
{
    /// <summary>Composes provider-neutral OpenIddict behavior without selecting a persistence provider.</summary>
    /// <param name="configureDbContext">
    /// Retained for source and binary compatibility with the pre-host-wire composite. Passing a callback now fails
    /// closed; the host must configure its selected OpenIddict provider explicitly before this method. This keeps
    /// provider ownership visible at the host boundary instead of silently reintroducing EF here.
    /// </param>
    public static IServiceCollection AddFoundationIdentityOpenIddict(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        if (configureDbContext is not null)
            throw new NotSupportedException(
                "The legacy configureDbContext callback is no longer accepted by AddFoundationIdentityOpenIddict. " +
                "Register OpenIddict's vendor store at the host boundary, then compose behavior.");

        return services.AddFoundationIdentityOpenIddictBehavior(configure);
    }
}
