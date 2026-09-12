using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.DependencyInjection;

public static class StructuredLogsEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddStructuredLogsEntityFrameworkCore(
        this IServiceCollection services,
        StructuredLogsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        services.TryAddSingleton(StructuredLogStoreBinding.Default);
        services.RemoveAll<InMemoryStructuredLogStore>();

        switch (provider)
        {
            case "sqlite":
                AddContext<StructuredLogsSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<StructuredLogsSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<StructuredLogsPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<StructuredLogsMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown Structured Logs EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.",
                    nameof(options));
        }

        services.AddSingleton(options);
        services.ReplaceDiagnosticsStore<IStructuredLogStore, EfStructuredLogStore>(ServiceLifetime.Singleton);
        services.AddDiagnosticsPersistenceLifecycle<EfStructuredLogStore>();
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        StructuredLogsEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : StructuredLogsDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
        {
            var connectionString = ResolveConnectionString(provider, options);
            bind(
                builder,
                connectionString,
                StructuredLogsEfModule.HistoryTableName,
                typeof(StructuredLogsDbContext).Assembly.GetName().Name);
        });
        services.AddScoped<StructuredLogsDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(
        IServiceProvider provider,
        StructuredLogsEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
                throw new InvalidOperationException($"Structured Logs EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            return named;
        }

        var fallback = configuration?.GetConnectionString(StructuredLogsEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return StructuredLogsEfModule.DefaultSqliteConnectionString;

        throw new InvalidOperationException("Structured Logs EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }
}

public sealed class StructuredLogsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
