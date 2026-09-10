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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecretsEfMigrationHostedService>());
        return services;
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
}

public sealed class SecretsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public EfMigratePolicy MigratePolicy { get; set; } = EfMigratePolicy.AutoMigrate;
}

public sealed class SecretsEfMigrationHostedService(
    IServiceScopeFactory scopes,
    SecretsEntityFrameworkCoreOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SecretsDbContext>();
        var expected = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        await EfDatabaseMigrator.ApplyAsync(context, expected, options.MigratePolicy, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
