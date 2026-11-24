using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>Composes the OpenIddict token store on a host's root container.</summary>
public static class OpenIddictStoreExtensions
{
    /// <summary>
    /// Registers the OpenIddict vendor EF Core store, its engine selection, Elsa's migration policy and token pruning, in the
    /// order they depend on each other. Call it once on the host container, before CShells is added, in a host whose shells
    /// enable <c>FoundationIdentityOpenIddict</c>: CShells copies these root registrations into every shell, so all shells
    /// read one store. The settings are the default shell's <c>FoundationIdentityOpenIddict</c> feature settings
    /// (<c>ConnectionString</c>, <c>AutoMigrate</c>, <c>Provider</c> and <c>Prune</c>).
    /// </summary>
    public static IServiceCollection AddOpenIddictEntityFrameworkCoreStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddOpenIddictVendorStore(configuration)
            .AddOpenIddictStoreEngines(configuration)
            .AddOpenIddictMigrationPolicy(configuration)
            .AddOpenIddictPruning(configuration);
    }
}
