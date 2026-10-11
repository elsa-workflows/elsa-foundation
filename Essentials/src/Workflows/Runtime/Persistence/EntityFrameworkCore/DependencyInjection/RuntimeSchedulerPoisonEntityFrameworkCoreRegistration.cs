using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core scheduler-poison store (R23).</summary>
/// <remarks>
/// This registration owns only <see cref="IWorkflowSchedulerPoisonStore"/>. The aggregate Runtime EF context can
/// be shared with R14-R18 and R20-R24, but scheduler poison is intentionally not added to the operational-state
/// backend contract set.
/// </remarks>
public static class RuntimeSchedulerPoisonEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeSchedulerPoisonEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = WorkflowSchedulerPoisonStoreBackend.Find(services);
            if (existing?.Name == WorkflowSchedulerPoisonStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(x => x.ImplementationInstance)
                    .OfType<RuntimeSchedulerPoisonEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime scheduler-poison EF persistence is already registered with different provider options.");
                RuntimeEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime scheduler poison",
                    existing.Owns,
                    RuntimeOperationalStateStoreBackend.Find(services) is { } existingOperational && existingOperational.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                        ? existingOperational.Owns
                        : null);
                return services;
            }

            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                WorkflowSchedulerPoisonStoreBackend.EnsureNoUnownedRegistrations(services);

            RuntimeEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);

            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in WorkflowSchedulerPoisonStoreBackend.CaptureSurfaceRegistrations(services).ToArray())
                services.Remove(descriptor);

            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                operational.EnsureOwnsRegisteredContracts(services);
            RuntimeEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime scheduler poison",
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);

            var configured = new RuntimeSchedulerPoisonEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling
            };
            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (RuntimeEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(RuntimeEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName, configured.Schema, configured.Pooling));
            foreach (var descriptor in RuntimeEfContextRegistration.ContextRegistrations(services, provider))
                if (!owned.Contains(descriptor))
                    owned.Add(descriptor);

            services.AddScoped<EfWorkflowSchedulerPoisonStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IWorkflowSchedulerPoisonStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfWorkflowSchedulerPoisonStore>());
            services.Add(contract);
            owned.Add(concrete);
            owned.Add(contract);
            WorkflowSchedulerPoisonStoreBackend.Register(
                services,
                new WorkflowSchedulerPoisonStoreBackend(
                    WorkflowSchedulerPoisonStoreBackend.EntityFramework,
                    owned,
                    collection => RemoveOwned(collection, owned)));
            commitExistingRemoval?.Invoke(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static bool OptionsEqual(
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions left,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName);

    private static void RemoveOwned(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> owned)
    {
        foreach (var descriptor in owned.Where(descriptor =>
                     services.Contains(descriptor) &&
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     DurableTimerStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeSchedulerPoisonEntityFrameworkCoreOptions
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
