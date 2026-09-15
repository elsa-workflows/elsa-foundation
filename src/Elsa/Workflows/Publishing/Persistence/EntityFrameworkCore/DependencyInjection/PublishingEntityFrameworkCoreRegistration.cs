using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;

public static class PublishingEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddPublishingEntityFrameworkCore(
        this IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = PublicationSnapshotReviewStoreBackend.Find(services);
            if (existing?.Name == PublicationSnapshotReviewStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                var prior = services.Select(service => service.ImplementationInstance)
                    .OfType<PublishingEntityFrameworkCoreOptions>().Single();
                if (!OptionsEqual(prior, options))
                    throw new InvalidOperationException("Publishing snapshot-review EF persistence is already registered with different provider options.");
                return services;
            }

            if (existing is not null)
                existing.RemoveOwnedArtifacts(services);
            else
                PublicationSnapshotReviewStoreBackend.EnsureNoUnownedRegistrations(services);

            var configured = new PublishingEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            };
            services.AddSingleton(configured);
            AddContext(services, configured, provider);
            services.AddScoped<EfPublicationSnapshotReviewStore>();
            var storeDescriptor = ServiceDescriptor.Scoped<IPublicationSnapshotReviewStore>(providerService =>
                providerService.GetRequiredService<EfPublicationSnapshotReviewStore>());
            services.Add(storeDescriptor);
            PublicationSnapshotReviewStoreBackend.Register(
                services,
                new PublicationSnapshotReviewStoreBackend(PublicationSnapshotReviewStoreBackend.EntityFramework, storeDescriptor));
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static void AddContext(IServiceCollection services, PublishingEntityFrameworkCoreOptions options, string provider)
    {
        switch (provider)
        {
            case "sqlite": AddContext<PublishingSnapshotReviewSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite); break;
            case "sqlserver": AddContext<PublishingSnapshotReviewSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer); break;
            case "postgresql": AddContext<PublishingSnapshotReviewPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql); break;
            case "mysql": AddContext<PublishingSnapshotReviewMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql); break;
            default: throw new ArgumentException($"Unknown Publishing EF provider '{options.Provider}'.", nameof(options));
        }
    }

    private static void AddContext<TContext>(IServiceCollection services, PublishingEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : PublishingSnapshotReviewDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
            bind(builder, ResolveConnection(provider, options), PublishingSnapshotReviewEfModule.HistoryTableName, typeof(PublishingSnapshotReviewDbContext).Assembly.GetName().Name));
        services.AddScoped<PublishingSnapshotReviewDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private static string ResolveConnection(IServiceProvider provider, PublishingEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName) ??
                   throw new InvalidOperationException($"Publishing EF connection '{options.ConnectionName}' was not found.");
        var fallback = configuration?.GetConnectionString(PublishingSnapshotReviewEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return PublishingSnapshotReviewEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Publishing snapshot-review EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private static bool OptionsEqual(PublishingEntityFrameworkCoreOptions left, PublishingEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        left.ConnectionString == right.ConnectionString && left.ConnectionName == right.ConnectionName;
}

public sealed class PublishingEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
