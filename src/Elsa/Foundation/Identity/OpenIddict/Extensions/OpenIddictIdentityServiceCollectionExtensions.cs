using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Composes the provider-neutral OpenIddict behavior with the transitional EF Core store oracle.
    /// </summary>
    /// <param name="configureDbContext">
    /// Configures the token-store provider. When <c>null</c>, a Sqlite database
    /// (<see cref="OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString"/> — the shared identity database file,
    /// with the module's own migrations-history table) is used, or an EF in-memory database when
    /// <see cref="OpenIddictIdentityOptions.IsDevelopmentOrDemo"/> is set.
    /// </param>
    public static IServiceCollection AddFoundationIdentityOpenIddict(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddFoundationIdentityOpenIddictBehavior(configure);
        services.AddOptions<OpenIddictEntityFrameworkCoreOptions>()
            .Configure<IOptions<OpenIddictIdentityOptions>>((options, legacyOptions) =>
            {
                options.ConnectionString = legacyOptions.Value.ConnectionString;
                options.AutoMigrate = legacyOptions.Value.AutoMigrate;
            });

        services.AddDbContext<OpenIddictIdentityDbContext>((serviceProvider, builder) =>
        {
            if (configureDbContext is not null)
                configureDbContext(builder);
            else if (serviceProvider.GetRequiredService<IOptions<OpenIddictIdentityOptions>>().Value.IsDevelopmentOrDemo)
                builder.UseInMemoryDatabase("elsa-identity-openiddict");
            else
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenIddictEntityFrameworkCoreOptions>>().Value;
                builder.UseSqlite(options.ConnectionString ?? OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString, sqlite => sqlite
                    .MigrationsAssembly(typeof(OpenIddictIdentityDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable, OpenIddictIdentityDbContext.Schema));
            }
        });

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>());

        // Expose the store initializer under both lifecycle hooks: IHostedService for plain hosts/tests and
        // the CShells IShellInitializer for the shell-composed Elsa.Server host (see the initializer's
        // remarks). It migrates the relational store (durable path) or ensure-creates the in-memory one
        // (dev/demo); idempotent under either hook. It runs on BOTH paths because the token store must exist
        // before the first issuance — unlike the identity substrate, there is no seed step to migrate it.
        services.AddSingleton<OpenIddictIdentityStoreInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenIddictIdentityStoreInitializer>());
        services.AddSingleton<CShells.Lifecycle.IShellInitializer>(sp => sp.GetRequiredService<OpenIddictIdentityStoreInitializer>());

        return services;
    }
}
