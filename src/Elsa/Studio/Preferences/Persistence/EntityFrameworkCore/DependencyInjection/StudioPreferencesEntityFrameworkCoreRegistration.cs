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
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<StudioPreferencesDbContext>(provider =>
            provider.GetRequiredService<TContext>());
    }
}

public sealed class StudioPreferencesEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
