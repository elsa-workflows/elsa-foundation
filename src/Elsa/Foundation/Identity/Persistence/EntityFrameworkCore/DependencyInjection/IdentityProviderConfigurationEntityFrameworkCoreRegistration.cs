using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in registration for only the tenant/global provider-configuration contracts.</summary>
public static class IdentityProviderConfigurationEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";

    public static IServiceCollection AddIdentityProviderConfigurationEntityFrameworkCore(
        this IServiceCollection services,
        IdentityProviderConfigurationEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var registration = new IdentityProviderConfigurationEfRegistration(provider, options.ConnectionString, options.ConnectionName);
        var existing = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityProviderConfigurationEfRegistration>()
            .SingleOrDefault();
        var existingBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProviderConfigurationStoreBackend>()
            .FirstOrDefault();
        if (existingBackend?.Name is "entity-framework")
        {
            if (existing is null || existing != registration)
                throw new InvalidOperationException("Identity provider-configuration EF persistence is already registered with different options.");
            return services;
        }
        if (existingBackend is not null && !string.Equals(existingBackend.Name, "groundwork", StringComparison.Ordinal))
            ProviderConfigurationStoreBackend.EnsureCompatible(existingBackend.Name, StoreBackendName);
        services.RemoveAll<ProviderConfigurationStoreBackend>();
        services.AddSingleton(new ProviderConfigurationStoreBackend(StoreBackendName));
        if (existing is not null && existing != registration)
            throw new InvalidOperationException("Identity provider-configuration EF persistence is already registered with different options.");
        if (existing is not null)
            return services;
        services.AddSingleton(registration);

        services.AddSingleton(options);
        services.RemoveAll<IProviderConfigurationStore>();
        services.RemoveAll<IRevisionAwareProviderConfigurationStore>();

        switch (provider)
        {
            case "sqlite":
                AddContext<IdentityProviderConfigurationSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<IdentityProviderConfigurationSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<IdentityProviderConfigurationPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<IdentityProviderConfigurationMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException($"Unknown Identity provider-configuration EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }

        services.AddScoped<EfProviderConfigurationStore>();
        services.AddScoped<IProviderConfigurationStore>(provider => provider.GetRequiredService<EfProviderConfigurationStore>());
        services.AddScoped<IRevisionAwareProviderConfigurationStore>(provider => provider.GetRequiredService<EfProviderConfigurationStore>());
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        IdentityProviderConfigurationEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : IdentityProviderConfigurationDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
            bind(builder, ResolveConnectionString(provider, options), IdentityProviderConfigurationEfModule.HistoryTableName, typeof(IdentityProviderConfigurationDbContext).Assembly.GetName().Name));
        services.AddScoped<IdentityProviderConfigurationDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(IServiceProvider provider, IdentityProviderConfigurationEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
                throw new InvalidOperationException($"Identity provider-configuration EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            return named;
        }

        var fallback = configuration?.GetConnectionString(IdentityProviderConfigurationEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return IdentityProviderConfigurationEfModule.DefaultSqliteConnectionString;

        throw new InvalidOperationException("Identity provider-configuration EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private sealed record IdentityProviderConfigurationEfRegistration(string Provider, string? ConnectionString, string? ConnectionName);
}

public sealed class IdentityProviderConfigurationEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
