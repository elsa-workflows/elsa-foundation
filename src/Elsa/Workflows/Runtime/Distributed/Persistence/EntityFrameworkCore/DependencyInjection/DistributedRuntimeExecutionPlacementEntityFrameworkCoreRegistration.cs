using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;

public static class DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = ExecutionPlacementStoreBackend.EntityFramework;

    public static IServiceCollection AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(
        this IServiceCollection services,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        var configuredOptions = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName
        };
        var registration = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity(configuredOptions, provider);

        var existingRegistration = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity>()
            .SingleOrDefault();
        if (existingRegistration is not null && existingRegistration != registration)
            throw new InvalidOperationException("Distributed runtime execution placement EF persistence is already registered with different options.");

        var existingBackend = ExecutionPlacementStoreBackend.Find(services);
        existingBackend?.EnsureOwnsRegisteredContract(services);

        if (ExecutionPlacementStoreBackend.HasRegisteredContract(services) && existingBackend is null)
            throw new InvalidOperationException("An explicit IExecutionPlacementStore is already registered; EF placement persistence refuses to replace it implicitly.");
        if (existingRegistration is not null)
        {
            if (existingBackend?.Name != StoreBackendName)
                throw new InvalidOperationException("Distributed runtime execution placement EF persistence registration is incomplete.");
            return services;
        }
        if (existingBackend?.Name == StoreBackendName)
            throw new InvalidOperationException("Distributed runtime execution placement EF persistence is already registered with different options.");

        existingBackend?.RemoveOwnedArtifacts(services);
        services.AddSingleton(registration);
        services.AddSingleton(configuredOptions);
        services.RemoveAll<ExecutionPlacementStoreBackend>();
        services.RemoveAll<IExecutionPlacementStore>();

        switch (provider)
        {
            case "sqlite":
                AddContext<ExecutionPlacementSqliteDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<ExecutionPlacementSqlServerDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<ExecutionPlacementPostgreSqlDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<ExecutionPlacementMySqlDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException($"Unknown distributed runtime execution placement EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }

        var placementDescriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore>(provider => new EfExecutionPlacementStore(
            provider.GetRequiredService<ExecutionPlacementDbContext>(),
            provider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextAccessor>()));
        services.Add(placementDescriptor);
        services.AddSingleton(new ExecutionPlacementStoreBackend(StoreBackendName, placementDescriptor));
        return services;
    }

    public static IServiceCollection AddExecutionPlacementEntityFrameworkCore(
        this IServiceCollection services,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options) =>
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(options);

    private static void AddContext<TContext>(
        IServiceCollection services,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : ExecutionPlacementDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => bind(
            builder,
            ResolveConnectionString(provider, options),
            ExecutionPlacementEfModule.HistoryTableName,
            typeof(ExecutionPlacementDbContext).Assembly.GetName().Name));
        services.AddScoped<ExecutionPlacementDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(
        IServiceProvider provider,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
                throw new InvalidOperationException($"Distributed runtime execution placement EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            return named;
        }
        var fallback = configuration?.GetConnectionString(ExecutionPlacementEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return ExecutionPlacementEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Distributed runtime execution placement EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }
}

public sealed class DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}

public sealed record DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity(
    string Provider,
    string? ConnectionString,
    string? ConnectionName)
{
    public DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity(
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options,
        string provider)
        : this(provider, options.ConnectionString, options.ConnectionName)
    {
    }

}
