using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in registration for only the tenant/global provider-configuration contracts.</summary>
public static class IdentityProviderConfigurationEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";
    private static readonly EfModuleBinding Binding = new(
        "Identity provider-configuration",
        IdentityProviderConfigurationEfModule.HistoryTableName,
        typeof(IdentityProviderConfigurationDbContext).Assembly.GetName().Name,
        IdentityProviderConfigurationEfModule.DefaultConnectionName,
        IdentityProviderConfigurationEfModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddIdentityProviderConfigurationEntityFrameworkCore(
        this IServiceCollection services,
        IdentityProviderConfigurationEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, IdentityProviderConfigurationEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<IdentityProviderConfigurationSqliteDbContext>,
            AddContext<IdentityProviderConfigurationSqlServerDbContext>,
            AddContext<IdentityProviderConfigurationPostgreSqlDbContext>,
            AddContext<IdentityProviderConfigurationMySqlDbContext>);
        var registration = new IdentityProviderConfigurationEfRegistration(provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);
        var existing = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityProviderConfigurationEfRegistration>()
            .SingleOrDefault();
        var existingBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProviderConfigurationStoreBackend>()
            .FirstOrDefault();
        if (existing is not null && existing != registration)
            throw new InvalidOperationException("Identity provider-configuration EF persistence is already registered with different options.");
        if (existingBackend?.Name is "entity-framework")
        {
            if (existing is null)
                throw new InvalidOperationException("Identity provider-configuration EF persistence is already registered with different options.");
            existingBackend.EnsureOwnsRegisteredContracts(services);
            return services;
        }
        if (existingBackend is not null)
            ProviderConfigurationStoreBackend.EnsureCompatible(existingBackend.Name, StoreBackendName);
        if (existingBackend is not null)
            existingBackend.EnsureOwnsRegisteredContracts(services);
        else if (services.Any(descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) ||
                                            descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore)))
            throw new InvalidOperationException("Identity provider-configuration EF persistence conflicts with an unowned host registration.");

        services.RemoveAll<ProviderConfigurationStoreBackend>();
        if (existing is not null)
            return services;
        services.AddSingleton(registration);

        services.AddFoundationIdentityAbstractions();
        services.AddPersistenceCore();
        services.AddSingleton(options);
        services.RemoveAll<IProviderConfigurationStore>();
        services.RemoveAll<IRevisionAwareProviderConfigurationStore>();

        addContext(services, options);

        services.AddScoped<EfProviderConfigurationStore>();
        var providerDescriptor = ServiceDescriptor.Scoped<IProviderConfigurationStore>(provider =>
            provider.GetRequiredService<EfProviderConfigurationStore>());
        var revisionDescriptor = ServiceDescriptor.Scoped<IRevisionAwareProviderConfigurationStore>(provider =>
            provider.GetRequiredService<EfProviderConfigurationStore>());
        services.Add(providerDescriptor);
        services.Add(revisionDescriptor);
        services.EnsureReplacementContract<IProviderConfigurationStore, EfProviderConfigurationStore>();
        services.EnsureReplacementContract<IRevisionAwareProviderConfigurationStore, EfProviderConfigurationStore>();
        services.AddSingleton(new ProviderConfigurationStoreBackend(StoreBackendName, providerDescriptor, revisionDescriptor));
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        IdentityProviderConfigurationEntityFrameworkCoreOptions options)
        where TContext : IdentityProviderConfigurationDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<IdentityProviderConfigurationDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private sealed record IdentityProviderConfigurationEfRegistration(string Provider, string? ConnectionString, string? ConnectionName, string? Schema, bool Pooling);
}

public sealed class IdentityProviderConfigurationEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
}
