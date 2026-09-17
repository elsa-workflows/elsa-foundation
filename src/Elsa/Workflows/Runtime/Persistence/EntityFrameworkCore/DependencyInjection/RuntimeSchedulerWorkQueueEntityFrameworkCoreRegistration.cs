using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core durable scheduler-work queue (R22).</summary>
public static class RuntimeSchedulerWorkQueueEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeSchedulerWorkQueueEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = SchedulerWorkQueueStoreBackend.Find(services);
            if (existing?.Name == SchedulerWorkQueueStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime scheduler-work EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime scheduler work",
                    existing.Owns,
                    RuntimeOperationalStateStoreBackend.Find(services) is { } existingOperational && existingOperational.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? existingOperational.Owns : null,
                    DurableTimerStoreBackend.Find(services) is { } existingTimers && existingTimers.Name == DurableTimerStoreBackend.EntityFramework ? existingTimers.Owns : null);
                return services;
            }


            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                SchedulerWorkQueueStoreBackend.EnsureNoUnownedRegistrations(services);

            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName);
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(
                services,
                options.RecoveryContinuationSigningKey,
                "Runtime scheduler work");

            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in SchedulerWorkQueueStoreBackend.CaptureQueueSurfaceRegistrations(services).ToArray())
                services.Remove(descriptor);

            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            var timers = DurableTimerStoreBackend.Find(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime scheduler work",
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null,
                timers?.Name == DurableTimerStoreBackend.EntityFramework ? timers.Owns : null);

            var configured = new RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(configuredOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey))
                    configuredOptions.SigningKey = options.RecoveryContinuationSigningKey;
                configuredOptions.AllowEphemeralDevelopmentKey = false;
            });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(BookmarkStateEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName));
            services.AddScoped<EfSchedulerWorkQueueStore>();
            var concrete = services.Last();
            services.AddScoped<IWorkflowSchedulerWorkQueue>(serviceProvider =>
                serviceProvider.GetRequiredService<EfSchedulerWorkQueueStore>());
            var queueContract = services.Last();
            services.AddScoped<IWorkflowSchedulerWorkClaimInspection>(serviceProvider =>
                serviceProvider.GetRequiredService<EfSchedulerWorkQueueStore>());
            var inspectionContract = services.Last();
            owned.AddRange([concrete, queueContract, inspectionContract]);
            owned.Add(EfRuntimeInfrastructureDurabilityEvidenceRegistration.AddOwned(
                services, WorkflowDispatchDurabilityComponents.Scheduler));
            SchedulerWorkQueueStoreBackend.Register(
                services,
                new SchedulerWorkQueueStoreBackend(
                    SchedulerWorkQueueStoreBackend.EntityFramework,
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
        RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions left,
        RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName) &&
        StringComparer.Ordinal.Equals(left.RecoveryContinuationSigningKey, right.RecoveryContinuationSigningKey);

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

public sealed class RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}
