using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;

public static class StudioPreferencesEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";

    public static IServiceCollection AddStudioPreferencesEntityFrameworkCore(
        this IServiceCollection services,
        StudioPreferencesEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var existingBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<StudioPreferenceStoreBackend>()
            .FirstOrDefault();
        StudioPreferenceStoreBackend.EnsureCompatible(existingBackend?.Name, StoreBackendName);
        if (existingBackend is null)
            services.AddSingleton(new StudioPreferenceStoreBackend(StoreBackendName));

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        services.AddSingleton(options);
        services.RemoveAll<IStudioPreferenceStore>();

        switch (provider)
        {
            case "sqlite":
                AddContext<StudioPreferencesSqliteDbContext>(
                    services, options, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<StudioPreferencesSqlServerDbContext>(
                    services, options, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<StudioPreferencesPostgreSqlDbContext>(
                    services, options, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<StudioPreferencesMySqlDbContext>(
                    services, options, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown Studio Preferences EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.",
                    nameof(options));
        }

        services.AddScoped<IStudioPreferenceStore>(provider =>
            new EfStudioPreferenceStore(provider.GetRequiredService<StudioPreferencesDbContext>()));
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        StudioPreferencesEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : StudioPreferencesDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
        {
            var connectionString = ResolveConnectionString(provider, options);
            bind(
                builder,
                connectionString,
                StudioPreferencesEfModule.HistoryTableName,
                typeof(StudioPreferencesDbContext).Assembly.GetName().Name);
        });
        services.AddScoped<StudioPreferencesDbContext>(provider =>
            provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(
        IServiceProvider provider,
        StudioPreferencesEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
            {
                throw new InvalidOperationException(
                    $"Studio Preferences EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            }

            return named;
        }

        var fallback = configuration?.GetConnectionString(StudioPreferencesEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return StudioPreferencesEfModule.DefaultSqliteConnectionString;

        throw new InvalidOperationException(
            "Studio Preferences EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }
}

public sealed class StudioPreferencesEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
