using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>
/// Host-composed wiring for OpenIddict's vendor Entity Framework Core store.
/// The OpenIddict feature itself remains provider-neutral and shell-composable; this host chooses the vendor
/// persistence implementation and owns its startup lifecycle explicitly.
/// </summary>
public static class OpenIddictVendorStoreRegistration
{
    public static IServiceCollection AddOpenIddictVendorStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenIddictIdentityOptions>(
            configuration.GetSection("CShells:Shells:default:Features:FoundationIdentityOpenIddict"));
        services.AddOptions<OpenIddictEntityFrameworkCoreOptions>()
            .Configure<IOptions<OpenIddictIdentityOptions>>((options, identityOptions) =>
            {
                options.ConnectionString = identityOptions.Value.ConnectionString;
                options.AutoMigrate = identityOptions.Value.AutoMigrate;
            });

        services.AddDbContext<OpenIddictIdentityDbContext>((serviceProvider, builder) =>
        {
            var identityOptions = serviceProvider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value;
            if (identityOptions.IsDevelopmentOrDemo)
            {
                builder.UseInMemoryDatabase("elsa-identity-openiddict");
                return;
            }

            var options = serviceProvider.GetRequiredService<IOptions<OpenIddictEntityFrameworkCoreOptions>>().Value;
            builder.UseSqlite(
                options.ConnectionString ?? OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(OpenIddictIdentityDbContext).Assembly)
                    .MigrationsHistoryTable(
                        OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable,
                        OpenIddictIdentityDbContext.Schema));
        });

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>());

        // Root hosted services run once for the host process. CShells copies these root descriptors into shell
        // providers, so registering this initializer again as an IShellInitializer would race durable migrations
        // during shell activation. The context, migrations, and lifecycle remain host-owned vendor infrastructure.
        services.AddSingleton<OpenIddictIdentityStoreInitializer>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OpenIddictIdentityStoreInitializer>());

        return services;
    }
}
