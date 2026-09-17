using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;

public static class DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = ExecutionCommandTransportBackend.EntityFramework;
    private static readonly EfModuleBinding Binding = new(
        "Distributed runtime execution command transport",
        ExecutionCommandTransportEfModule.HistoryTableName,
        typeof(ExecutionCommandTransportDbContext).Assembly.GetName().Name,
        ExecutionCommandTransportEfModule.DefaultConnectionName,
        ExecutionCommandTransportEfModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(
        this IServiceCollection services,
        DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<ExecutionCommandTransportSqliteDbContext>,
            AddContext<ExecutionCommandTransportSqlServerDbContext>,
            AddContext<ExecutionCommandTransportPostgreSqlDbContext>,
            AddContext<ExecutionCommandTransportMySqlDbContext>);
        var configuredOptions = new DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName
        };
        var registration = new DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistrationIdentity(configuredOptions, provider);
        var existingRegistration = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistrationIdentity>()
            .SingleOrDefault();
        if (existingRegistration is not null && existingRegistration != registration)
            throw new InvalidOperationException("Distributed runtime execution command transport EF persistence is already registered with different options.");

        var existingBackend = ExecutionCommandTransportBackend.Find(services);
        existingBackend?.EnsureOwnsRegisteredContract(services);
        if (ExecutionCommandTransportBackend.HasRegisteredContract(services) && existingBackend is null)
            throw new InvalidOperationException("An explicit IExecutionCommandTransport is already registered; EF command transport persistence refuses to replace it implicitly.");
        if (existingRegistration is not null)
        {
            if (existingBackend?.Name != StoreBackendName)
                throw new InvalidOperationException("Distributed runtime execution command transport EF persistence registration is incomplete.");
            return services;
        }
        if (existingBackend?.Name == StoreBackendName)
            throw new InvalidOperationException("Distributed runtime execution command transport EF persistence is already registered with different options.");

        existingBackend?.RemoveOwnedArtifacts(services);
        services.AddSingleton(registration);
        services.AddSingleton(configuredOptions);
        services.RemoveAll<ExecutionCommandTransportBackend>();
        services.RemoveAll<IExecutionCommandTransport>();

        addContext(services, configuredOptions);

        var transportDescriptor = ServiceDescriptor.Scoped<IExecutionCommandTransport>(provider => new EfExecutionCommandTransport(
            provider.GetRequiredService<ExecutionCommandTransportDbContext>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.Add(transportDescriptor);
        ExecutionCommandTransportBackend.Register(services, new ExecutionCommandTransportBackend(StoreBackendName, transportDescriptor));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, EntityFrameworkCommandTransportDurabilityEvidence>());
        return services;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions options)
        where TContext : ExecutionCommandTransportDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<ExecutionCommandTransportDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}

public sealed record DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistrationIdentity(
    string Provider,
    string? ConnectionString,
    string? ConnectionName)
{
    public DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistrationIdentity(
        DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions options,
        string provider)
        : this(provider, options.ConnectionString, options.ConnectionName)
    {
    }
}

internal sealed class EntityFrameworkCommandTransportDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DistributionPersistence;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}
