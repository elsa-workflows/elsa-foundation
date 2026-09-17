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
    private static readonly EfModuleBinding Binding = new(
        "Structured Logs",
        StructuredLogsEfModule.HistoryTableName,
        typeof(StructuredLogsDbContext).Assembly.GetName().Name,
        StructuredLogsEfModule.DefaultConnectionName,
        StructuredLogsEfModule.DefaultSqliteConnectionString);

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
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<StructuredLogsDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class StructuredLogsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
