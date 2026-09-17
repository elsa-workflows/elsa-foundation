using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;

public static class SecretsEntityFrameworkCoreRegistration
{
    private const string RepositoryBackendName = "entity-framework";
    private static readonly EfModuleBinding Binding = new(
        "Secrets",
        SecretsEfModule.HistoryTableName,
        typeof(SecretsDbContext).Assembly.GetName().Name,
        SecretsEfModule.DefaultConnectionName,
        SecretsEfModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddSecretsEntityFrameworkCore(
        this IServiceCollection services,
        SecretsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var addContext = Binding.Select<Action<IServiceCollection, SecretsEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<SecretsSqliteDbContext>,
            AddContext<SecretsSqlServerDbContext>,
            AddContext<SecretsPostgreSqlDbContext>,
            AddContext<SecretsMySqlDbContext>);

        services.AddSingleton(options);
        SelectSecretRepositoryBackend(services, RepositoryBackendName);
        services.RemoveAll<ISecretRepository>();
        addContext(services, options);

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

    private static void AddContext<TContext>(IServiceCollection services, SecretsEntityFrameworkCoreOptions options)
        where TContext : SecretsDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<SecretsDbContext>(provider => provider.GetRequiredService<TContext>());
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
