using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore;
using ElsaOpenIddictEntityFrameworkCoreOptions = Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore.OpenIddictEntityFrameworkCoreOptions;

namespace Elsa.Workbench;

/// <summary>
/// Workbench-owned wiring for OpenIddict's vendor Entity Framework Core store.
/// The OpenIddict feature itself remains provider-neutral and shell-composable; this host chooses the vendor
/// persistence implementation and owns its startup lifecycle explicitly.
/// </summary>
internal static class WorkbenchOpenIddictVendorRegistration
{
    internal static IServiceCollection AddWorkbenchOpenIddictVendor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenIddictIdentityOptions>(
            configuration.GetSection("CShells:Shells:default:Features:FoundationIdentityOpenIddict"));
        services.AddOptions<ElsaOpenIddictEntityFrameworkCoreOptions>()
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

            var options = serviceProvider.GetRequiredService<IOptions<ElsaOpenIddictEntityFrameworkCoreOptions>>().Value;
            builder.UseSqlite(
                options.ConnectionString ?? OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(OpenIddictIdentityDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(
                        OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable,
                        OpenIddictIdentityDbContext.Schema));
        });

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>());

        // Root hosted services run once for the Workbench process. CShells copies these root descriptors into shell
        // providers, so registering this initializer again as an IShellInitializer would race durable migrations
        // during shell activation. The frozen context/migrations/initializer remain until #1471 removes them.
        services.AddSingleton<OpenIddictIdentityStoreInitializer>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OpenIddictIdentityStoreInitializer>());

        return services;
    }
}
