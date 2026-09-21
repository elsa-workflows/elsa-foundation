using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.DependencyInjection;

public static class StructuredLogsEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(StructuredLogsDbContext));

    public static IServiceCollection AddStructuredLogsEntityFrameworkCore(
        this IServiceCollection services,
        StructuredLogsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var addContext = Binding.Select<Action<IServiceCollection, StructuredLogsEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<StructuredLogsSqliteDbContext>,
            AddContext<StructuredLogsSqlServerDbContext>,
            AddContext<StructuredLogsPostgreSqlDbContext>,
            AddContext<StructuredLogsMySqlDbContext>);
        services.TryAddSingleton(StructuredLogStoreBinding.Default);
        services.RemoveAll<InMemoryStructuredLogStore>();

        addContext(services, options);

        services.AddSingleton(options);
        services.ReplaceDiagnosticsStore<IStructuredLogStore, EfStructuredLogStore>(ServiceLifetime.Singleton);
        services.AddDiagnosticsPersistenceLifecycle<EfStructuredLogStore>();
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        StructuredLogsEntityFrameworkCoreOptions options)
        where TContext : StructuredLogsDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<StructuredLogsDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class StructuredLogsEntityFrameworkCoreOptions
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
