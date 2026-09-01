using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>
/// Test-only host wiring for the frozen OpenIddict EF oracle. Production ownership lives in Elsa.Workbench; keeping
/// this setup here lets the behavior and contract suites exercise an explicit vendor choice without teaching the
/// provider-neutral Elsa composition extension about EF.
/// </summary>
internal static class OpenIddictVendorTestRegistration
{
    internal static IServiceCollection AddOpenIddictVendorForTests(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(configureDbContext);
        services.AddOptions<OpenIddictEntityFrameworkCoreOptions>();
        services.AddDbContext<OpenIddictIdentityDbContext>((_, builder) => configureDbContext(builder));

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>());

        services.AddSingleton<OpenIddictIdentityStoreInitializer>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OpenIddictIdentityStoreInitializer>());

        return services;
    }
}
