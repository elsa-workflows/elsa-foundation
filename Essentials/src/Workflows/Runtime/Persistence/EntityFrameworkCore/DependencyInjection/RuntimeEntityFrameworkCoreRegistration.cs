using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Persistence.EntityFramework;

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
/// participant rejects the transition.
/// </para>
/// <para>
/// The slot authority, the trigger-binding store and the recurring-schedule store serve slots only through the
/// activation switch this aggregate composes with them, which commits them together (#2230). Selecting any of them
/// through its own extension leaves the in-memory switch beside an EF store, a mixed composition that shell start refuses
/// (<see cref="WorkflowActivationSwitchCompositionValidator"/>); their extensions exist for the aggregate and for tests
/// of one store. The other participants' extensions compose on their own.
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
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            services.AddRuntimeOperationalStateEntityFrameworkCore(operational);
            services.AddRuntimeArtifactsEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                WorkflowExecutableCache = executableCache
            });
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeActivityExecutionEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                HierarchyCursorSigningKey = options.HierarchyCursorSigningKey,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeBookmarksEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling
            });
            services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddRuntimeSchedulerPoisonEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling
            });
            services.AddRuntimeDurableTimerEntityFrameworkCore(new()
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
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
            UseEntityFrameworkActivationSwitch(services);

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

    /// <summary>
    /// The slot authority, both projection stores and the source-reference store are EF over the one shared context by now,
    /// so a slot and its serving projections switch in one transaction of it (#2230). The in-memory switch the Runtime
    /// composes by default could not commit them, so it is replaced; a switch registered by anyone else is refused. The
    /// switch stamps retirements with the host's clock, which the Runtime composition root registers too.
    /// </summary>
    private static void UseEntityFrameworkActivationSwitch(IServiceCollection services)
    {
        var switches = services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowActivationSwitch)).ToArray();
        if (switches.Length == 1 && switches[0].ImplementationType == typeof(EfWorkflowActivationSwitch))
            return;
        if (switches.Any(descriptor => descriptor.ImplementationType != typeof(InMemoryWorkflowActivationSwitch)))
            throw new InvalidOperationException("An explicit workflow activation switch registration is already present; Runtime EF persistence refuses to replace it implicitly.");

        foreach (var descriptor in switches)
            services.Remove(descriptor);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IWorkflowActivationSwitch, EfWorkflowActivationSwitch>();
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

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
    public string? HierarchyCursorSigningKey { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }

    /// <summary>Whether ordinary scoped reads of immutable workflow executables go through a bounded shell-local cache.</summary>
    public bool CacheWorkflowExecutables { get; set; } = true;

    /// <summary>Maximum executables the cache retains; must be positive when caching is enabled.</summary>
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;
}
