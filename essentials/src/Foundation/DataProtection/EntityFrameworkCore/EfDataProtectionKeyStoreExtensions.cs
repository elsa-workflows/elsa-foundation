using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

public static class EfDataProtectionKeyStoreExtensions
{
    /// <summary>Data Protection's own hosted service, which loads the key ring as the host starts.</summary>
    private const string KeyRingLoaderTypeName = "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService";

    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(DataProtectionKeysDbContext));

    /// <summary>
    /// Persists the host's key ring to the EF key store (#2191), so every host that reaches the same table shares it.
    /// </summary>
    /// <remarks>
    /// Call it on the host container, never from a shell feature: the key ring is the host's, as its cluster membership is.
    /// Shell containers are built from copies of the host's registrations, and each resolves the one store the host built,
    /// through <see cref="ShellServiceSharingExtensions.ShareWithShells{TService}"/>, so a shell reads the host's connection
    /// rather than its own configuration's. The module migrates through the plain-host migrator only, as the host starts and
    /// before it serves a request, so a shell activation never migrates it again. A host composes it once; a second call is
    /// refused rather than replacing the first.
    /// </remarks>
    public static IDataProtectionBuilder PersistKeysToElsaDatabase(this IDataProtectionBuilder builder, EfDataProtectionKeyStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        var services = builder.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EfDataProtectionKeyRepository)))
            throw new InvalidOperationException("The Data Protection key store is already composed on this host. Compose it once, on the host container.");

        var addContext = Binding.Select<Action<IServiceCollection, EfDataProtectionKeyStoreOptions>>(
            options.Provider,
            AddContext<DataProtectionKeysSqliteDbContext>,
            AddContext<DataProtectionKeysSqlServerDbContext>,
            AddContext<DataProtectionKeysPostgreSqlDbContext>,
            AddContext<DataProtectionKeysMySqlDbContext>);
        addContext(services, options);
        services.AddEfModuleHostMigrations<DataProtectionKeysDbContext>(options.Provider);
        StartKeyRingLoadingAfterMigrations(services);

        services.AddSingleton<EfDataProtectionKeyRepository>().ShareWithShells<EfDataProtectionKeyRepository>();
        // Resolved in each container that builds a key manager; in a shell the store it hands over is the host's.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(keys => keys.XmlRepository = provider.GetRequiredService<EfDataProtectionKeyRepository>()));
        return builder;
    }

    /// <summary>
    /// Data Protection loads the key ring as the host starts, from a hosted service of its own that every
    /// <c>AddDataProtection</c> call registers once, ahead of this module's migrator. Started first, it would read the key table
    /// before the migrator has created it on the host's first start, and log the key ring as failed to load. Hosted services
    /// start in registration order, so it is moved behind the migrator, and reads the table the host has just made current.
    /// </summary>
    /// <remarks>
    /// The loader is internal to ASP.NET Core and implements nothing but <see cref="IHostedService"/>, so its full type name, in
    /// the Data Protection assembly, is the most precise handle the framework offers. Should a framework release rename it,
    /// nothing is moved and the host still starts, logging the first load as failed; <c>KeyRingStartupOrderTests</c> fails
    /// then, naming the loader it no longer finds.
    /// </remarks>
    private static void StartKeyRingLoadingAfterMigrations(IServiceCollection services)
    {
        var dataProtection = typeof(KeyManagementOptions).Assembly;
        foreach (var loader in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                                             descriptor.ImplementationType is { } type &&
                                                             type.Assembly == dataProtection &&
                                                             type.FullName == KeyRingLoaderTypeName).ToArray())
        {
            services.Remove(loader);
            services.Add(loader);
        }
    }


    private static void AddContext<TContext>(IServiceCollection services, EfDataProtectionKeyStoreOptions options)
        where TContext : DataProtectionKeysDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<DataProtectionKeysDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}
