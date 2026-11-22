using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;

public static class DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = ExecutionPlacementStoreBackend.EntityFramework;
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(ExecutionPlacementDbContext));

    public static IServiceCollection AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(
        this IServiceCollection services,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<ExecutionPlacementSqliteDbContext>,
            AddContext<ExecutionPlacementSqlServerDbContext>,
            AddContext<ExecutionPlacementPostgreSqlDbContext>,
            AddContext<ExecutionPlacementMySqlDbContext>);
        var configuredOptions = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName,
            Schema = options.Schema,
            Pooling = options.Pooling
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

        addContext(services, configuredOptions);

        var placementDescriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore>(provider => new EfExecutionPlacementStore(
            provider.GetRequiredService<ExecutionPlacementDbContext>(),
            provider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextAccessor>()));
        services.Add(placementDescriptor);
        ExecutionPlacementStoreBackend.Register(
            services,
            new ExecutionPlacementStoreBackend(StoreBackendName, placementDescriptor));
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options)
        where TContext : ExecutionPlacementDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<ExecutionPlacementDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
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

public sealed record DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity(
    string Provider,
    string? ConnectionString,
    string? ConnectionName,
    string? Schema,
    bool Pooling)
{
    public DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistrationIdentity(
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options,
        string provider)
        : this(provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling)
    {
    }

}
