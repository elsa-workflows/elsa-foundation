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
            var reviewBackend = PublicationSnapshotReviewStoreBackend.Find(services);
            var policyProjectionBackend = PublicationPolicyProjectionStoreBackend.Find(services);

            if (reviewBackend?.Name == PublicationSnapshotReviewStoreBackend.EntityFramework)
                reviewBackend.EnsureOwnsRegisteredContract(services);
            if (policyProjectionBackend?.Name == PublicationPolicyProjectionStoreBackend.EntityFramework)
                policyProjectionBackend.EnsureOwnsRegisteredContracts(services);
            if (policyProjectionBackend?.Name == PublicationPolicyProjectionStoreBackend.EntityFramework &&
                reviewBackend?.Name != PublicationSnapshotReviewStoreBackend.EntityFramework)
                throw new InvalidOperationException("Publication policy/projection-intent EF persistence requires the P04 snapshot-review EF context to be selected as well.");

            var existingOptions = services.Select(service => service.ImplementationInstance)
                .OfType<PublishingEntityFrameworkCoreOptions>()
                .SingleOrDefault();
            if (existingOptions is not null &&
                (reviewBackend?.Name != PublicationSnapshotReviewStoreBackend.EntityFramework || !OptionsEqual(existingOptions, options)))
                throw new InvalidOperationException("Publishing EF persistence is already registered with different provider options.");
            if (reviewBackend?.Name == PublicationSnapshotReviewStoreBackend.EntityFramework &&
                policyProjectionBackend?.Name == PublicationPolicyProjectionStoreBackend.EntityFramework)
                return services;

            if (reviewBackend?.Name != PublicationSnapshotReviewStoreBackend.EntityFramework)
            {
                if (reviewBackend is not null)
                    reviewBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationSnapshotReviewStoreBackend.EnsureNoUnownedRegistrations(services);
            }
            if (policyProjectionBackend?.Name != PublicationPolicyProjectionStoreBackend.EntityFramework)
            {
                if (policyProjectionBackend is not null)
                    policyProjectionBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationPolicyProjectionStoreBackend.EnsureNoUnownedRegistrations(services);
            }

            var policyProjectionOwned = new List<ServiceDescriptor>();
            if (reviewBackend?.Name != PublicationSnapshotReviewStoreBackend.EntityFramework)
            {
                var configured = new PublishingEntityFrameworkCoreOptions
                {
                    Provider = options.Provider,
                    ConnectionString = options.ConnectionString,
                    ConnectionName = options.ConnectionName
                };
                var optionsDescriptor = ServiceDescriptor.Singleton(configured);
                services.Add(optionsDescriptor);
                var reviewOwned = new List<ServiceDescriptor> { optionsDescriptor };
                reviewOwned.AddRange(AddContext(services, configured, provider));

                var reviewConcrete = new ServiceDescriptor(typeof(EfPublicationSnapshotReviewStore), typeof(EfPublicationSnapshotReviewStore), ServiceLifetime.Scoped);
                services.Add(reviewConcrete);
                reviewOwned.Add(reviewConcrete);
                var reviewContract = ServiceDescriptor.Scoped<IPublicationSnapshotReviewStore>(providerService =>
                    providerService.GetRequiredService<EfPublicationSnapshotReviewStore>());
                services.Add(reviewContract);
                reviewOwned.Add(reviewContract);
                PublicationSnapshotReviewStoreBackend.Register(
                    services,
                    new PublicationSnapshotReviewStoreBackend(PublicationSnapshotReviewStoreBackend.EntityFramework, reviewOwned));
            }

            var policyConcrete = new ServiceDescriptor(typeof(EfPublicationPolicyStore), typeof(EfPublicationPolicyStore), ServiceLifetime.Scoped);
            var intentConcrete = new ServiceDescriptor(typeof(EfPublicationProjectionIntentStore), typeof(EfPublicationProjectionIntentStore), ServiceLifetime.Scoped);
            services.Add(policyConcrete);
            services.Add(intentConcrete);
            var policyContract = ServiceDescriptor.Scoped<IPublicationPolicyStore>(providerService =>
                providerService.GetRequiredService<EfPublicationPolicyStore>());
            var intentContract = ServiceDescriptor.Scoped<IPublicationProjectionIntentStore>(providerService =>
                providerService.GetRequiredService<EfPublicationProjectionIntentStore>());
            services.Add(policyContract);
            services.Add(intentContract);
            policyProjectionOwned.Add(policyConcrete);
            policyProjectionOwned.Add(intentConcrete);
            policyProjectionOwned.Add(policyContract);
            policyProjectionOwned.Add(intentContract);
            PublicationPolicyProjectionStoreBackend.Register(
                services,
                new PublicationPolicyProjectionStoreBackend(
                    PublicationPolicyProjectionStoreBackend.EntityFramework,
                    policyProjectionOwned));
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

    private static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options,
        string provider)
    {
        return provider switch
        {
            "sqlite" => AddContext<PublishingSnapshotReviewSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite),
            "sqlserver" => AddContext<PublishingSnapshotReviewSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer),
            "postgresql" => AddContext<PublishingSnapshotReviewPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql),
            "mysql" => AddContext<PublishingSnapshotReviewMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql),
            _ => throw new ArgumentException($"Unknown Publishing EF provider '{options.Provider}'.", nameof(options))
        };
    }

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(
        IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : PublishingSnapshotReviewDbContext
    {
        var start = services.Count;
        services.AddDbContext<TContext>((provider, builder) =>
            bind(builder, ResolveConnection(provider, options), PublishingSnapshotReviewEfModule.HistoryTableName, typeof(PublishingSnapshotReviewDbContext).Assembly.GetName().Name));
        services.AddScoped<PublishingSnapshotReviewDbContext>(provider => provider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    private static string ResolveConnection(IServiceProvider provider, PublishingEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString!;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName!) ??
                   throw new InvalidOperationException($"Publishing EF connection '{options.ConnectionName}' was not found.");
        var fallback = configuration?.GetConnectionString(PublishingSnapshotReviewEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback!;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return PublishingSnapshotReviewEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Publishing EF persistence requires ConnectionString or ConnectionName for a non-Sqlite provider.");
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
