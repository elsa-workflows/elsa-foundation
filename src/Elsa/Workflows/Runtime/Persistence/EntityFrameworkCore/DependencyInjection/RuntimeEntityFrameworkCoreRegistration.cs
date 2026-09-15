using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Atomically composes all EF-owned Runtime checkpoint participants.</summary>
/// <remarks>
/// The individual participant extensions remain useful for opt-in, mixed compositions that do not use the
/// checkpoint writer. This aggregate is the only supported Groundwork-to-EF path for the transactionally coupled
/// Runtime family: it keeps the transition token private to the synchronous composition and restores both service
/// descriptors and provider-owned registration snapshots when any participant rejects the transition.
/// </remarks>
public static class RuntimeEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var selectedCheckpoint = RuntimeCheckpointCommitStoreBackend.Find(services);
        if (selectedCheckpoint?.Name != RuntimeCheckpointCommitStoreBackend.Groundwork)
            throw new InvalidOperationException(
                "Runtime EF persistence requires the aggregate Groundwork checkpoint backend before replacing the Runtime composition.");
        selectedCheckpoint.EnsureOwnsRegisteredContract(services);

        var serviceSnapshot = services.ToArray();
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();

        using var transition = RuntimeEfCheckpointCompositionTransition.Begin(services);
        try
        {
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
                ConnectionName = options.ConnectionName
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

    internal static void EnsureGroundworkCheckpointTransitionAllowed(IServiceCollection services, string participant)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(participant);
        if (!IsActive(services) && RuntimeCheckpointCommitStoreBackend.Find(services)?.Name == RuntimeCheckpointCommitStoreBackend.Groundwork)
            throw new InvalidOperationException($"Runtime {participant} EF persistence requires the aggregate EF Runtime transition while the Groundwork checkpoint writer is selected.");
    }

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
}
