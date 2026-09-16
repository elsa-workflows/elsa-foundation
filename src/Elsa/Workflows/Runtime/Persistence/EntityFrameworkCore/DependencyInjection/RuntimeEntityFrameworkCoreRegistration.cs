using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Atomically composes the complete EF Runtime persistence family (R01-R29).</summary>
/// <remarks>
/// <para>
/// This is the aggregate EF runtime composition. It composes on a service collection with no
/// runtime persistence selected, over the in-memory Runtime defaults, or as the only supported
/// switch for the transactionally coupled Runtime family; repeating it with the same options changes nothing.
/// </para>
/// <para>
/// Either every participant is selected or none is: the transition token stays private to the synchronous
/// composition, and both service descriptors and provider-owned registration snapshots are restored when any
/// participant rejects the transition. The individual participant extensions remain useful for opt-in, mixed
/// compositions that do not use the checkpoint writer.
/// </para>
/// </remarks>
public static class RuntimeEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var executableCache = new WorkflowExecutableCacheOptions
        {
            Enabled = options.CacheWorkflowExecutables,
            Capacity = options.WorkflowExecutableCacheCapacity
        };
        executableCache.Validate();
        // A selected checkpoint writer is either replaced (in-memory) or repeated (EF); either way it must still
        // own its contract. With none selected the composition starts from the Runtime defaults or from nothing.
        RuntimeCheckpointCommitStoreBackend.Find(services)?.EnsureOwnsRegisteredContract(services);

        var serviceSnapshot = services.ToArray();
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();

        using var transition = RuntimeEfCheckpointCompositionTransition.Begin(services);
        try
        {
            // Shared infrastructure rather than an EF-owned descriptor: every store binds its access context here.
            services.AddPersistenceCore();
            var operational = new RuntimeOperationalStateEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            services.AddRuntimeOperationalStateEntityFrameworkCore(operational);
            services.AddRuntimeArtifactsEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                WorkflowExecutableCache = executableCache
            });
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeActivityExecutionEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                HierarchyCursorSigningKey = options.HierarchyCursorSigningKey,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeBookmarksEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            });
            services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeSchedulerPoisonEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            });
            services.AddRuntimeDurableTimerEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
            services.AddRuntimePostCommitOutboxEntityFrameworkCore();
            services.AddRuntimeCheckpointCommitEntityFrameworkCore();
            // The trigger-binding and recurring-schedule tables carry their own projection state (R29); selecting both
            // withdraws the publication projection-state unit they replace.
            services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore();
            services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore();
            services.AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore();

            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in serviceSnapshot)
                services.Add(descriptor);
            foreach (var snapshot in registrationSnapshots)
                snapshot.Rollback();
            throw;
        }
    }
}

internal static class RuntimeEfCheckpointCompositionTransition
{
    internal static bool IsActive(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ImplementationInstance is Marker);

    internal static IDisposable Begin(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var marker = new Marker();
        services.AddSingleton(marker);
        return new Scope(services, marker);
    }

    private sealed class Marker
    {
    }

    private sealed class Scope(IServiceCollection services, Marker marker) : IDisposable
    {
        public void Dispose()
        {
            for (var index = services.Count - 1; index >= 0; index--)
                if (ReferenceEquals(services[index].ImplementationInstance, marker))
                    services.RemoveAt(index);
        }
    }
}

public sealed class RuntimeEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? HierarchyCursorSigningKey { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }

    /// <summary>Whether ordinary scoped reads of immutable workflow executables go through a bounded shell-local cache.</summary>
    public bool CacheWorkflowExecutables { get; set; } = true;

    /// <summary>Maximum executables the cache retains; must be positive when caching is enabled.</summary>
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;
}
