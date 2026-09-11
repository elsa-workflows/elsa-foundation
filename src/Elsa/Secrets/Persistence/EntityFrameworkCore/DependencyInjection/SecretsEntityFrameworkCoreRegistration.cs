using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;

public static class SecretsEntityFrameworkCoreRegistration
{
    private const string RepositoryBackendName = "entity-framework";

    public static IServiceCollection AddSecretsEntityFrameworkCore(
        this IServiceCollection services,
        SecretsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var historyTable = SecretsEfModule.HistoryTableName;
        var migrationsAssembly = typeof(SecretsDbContext).Assembly.GetName().Name;

        services.AddSingleton(options);
        SelectSecretRepositoryBackend(services, RepositoryBackendName);
        services.RemoveAll<ISecretRepository>();

        switch (provider)
        {
            case "sqlite":
                AddContext<SecretsSqliteDbContext>(services, options, historyTable, migrationsAssembly, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<SecretsSqlServerDbContext>(services, options, historyTable, migrationsAssembly, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<SecretsPostgreSqlDbContext>(services, options, historyTable, migrationsAssembly, EfRelationalProviderBinding.UseNpgsql);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown Secrets EF provider '{options.Provider}'. Expected Sqlite, SqlServer, or PostgreSql.",
                    nameof(options));
        }

        services.AddScoped<ISecretRepository>(sp => new EfSecretRepository(sp.GetRequiredService<SecretsDbContext>()));
        AddMigrationLifecycle(services);
        return services;
    }

    private static void AddMigrationLifecycle(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SecretsEfMigrationHostedService)))
            return;

        services.AddSingleton<SecretsEfMigrationHostedService>();
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<SecretsEfMigrationHostedService>());
        services.AddSingleton<IShellInitializer>(provider =>
            provider.GetRequiredService<SecretsEfMigrationHostedService>());
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        SecretsEntityFrameworkCoreOptions options,
        string historyTable,
        string? migrationsAssembly,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : SecretsDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
        {
            var connectionString = ResolveConnectionString(provider, options);
            bind(builder, connectionString, historyTable, migrationsAssembly);
        });
        services.AddScoped<SecretsDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(IServiceProvider provider, SecretsEntityFrameworkCoreOptions options)
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
                    $"Secrets EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            }

            return named;
        }

        var fallback = configuration?.GetConnectionString(SecretsEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return SecretsEfModule.DefaultSqliteConnectionString;

        throw new InvalidOperationException(
            "Secrets EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    internal static void SelectSecretRepositoryBackend(IServiceCollection services, string backend)
    {
        var existing = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()
            .FirstOrDefault();
        SecretRepositoryBackend.EnsureCompatible(existing?.Name, backend);
        if (existing is null)
            services.AddSingleton(new SecretRepositoryBackend(backend));
    }
}

public sealed class SecretsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public EfMigratePolicy MigratePolicy { get; set; } = EfMigratePolicy.AutoMigrate;
}
