using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;

public static class StudioPreferencesEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";
    private static readonly EfModuleBinding Binding = new(
        "Studio Preferences",
        StudioPreferencesEfModule.HistoryTableName,
        typeof(StudioPreferencesDbContext).Assembly.GetName().Name,
        StudioPreferencesEfModule.DefaultConnectionName,
        StudioPreferencesEfModule.DefaultSqliteConnectionString);

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

        var addContext = Binding.Select<Action<IServiceCollection, StudioPreferencesEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<StudioPreferencesSqliteDbContext>,
            AddContext<StudioPreferencesSqlServerDbContext>,
            AddContext<StudioPreferencesPostgreSqlDbContext>,
            AddContext<StudioPreferencesMySqlDbContext>);
        services.AddSingleton(options);
        services.RemoveAll<IStudioPreferenceStore>();

        addContext(services, options);

        services.AddScoped<IStudioPreferenceStore>(provider =>
            new EfStudioPreferenceStore(provider.GetRequiredService<StudioPreferencesDbContext>()));
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        StudioPreferencesEntityFrameworkCoreOptions options)
        where TContext : StudioPreferencesDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<StudioPreferencesDbContext>(provider =>
            provider.GetRequiredService<TContext>());
    }
}

public sealed class StudioPreferencesEntityFrameworkCoreOptions
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
