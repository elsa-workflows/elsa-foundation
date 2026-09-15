using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Explicit opt-in to the transaction-owning EF Runtime checkpoint writer.</summary>
public static class RuntimeCheckpointCommitEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeCheckpointCommitEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        try
        {
            RuntimeEfCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "checkpoint commit");
            RequireEfParticipants(services);
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime checkpoint EF persistence");

            var selected = RuntimeCheckpointCommitStoreBackend.Find(services);
            if (selected?.Name == RuntimeCheckpointCommitStoreBackend.EntityFramework)
            {
                selected.EnsureOwnsRegisteredContract(services);
                return services;
            }

            var contracts = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore)).ToArray();
            if (selected is null && (contracts.Length > 1 ||
                                     contracts.Any(descriptor => !RuntimeCheckpointCommitStoreBackend.IsRuntimeDefault(descriptor))))
                throw new InvalidOperationException("Runtime checkpoint EF persistence refuses to replace an unowned checkpoint writer.");

            selected?.RemoveOwnedRegistrations(services);
            foreach (var descriptor in contracts)
                services.Remove(descriptor);

            services.TryAddScoped<EfRuntimeCheckpointCommitStore>();
            var concrete = services.Single(descriptor => descriptor.ServiceType == typeof(EfRuntimeCheckpointCommitStore));
            var contract = ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(provider =>
                provider.GetRequiredService<EfRuntimeCheckpointCommitStore>());
            services.Add(contract);
            RuntimeCheckpointCommitStoreBackend.Register(services,
                new(RuntimeCheckpointCommitStoreBackend.EntityFramework, contract, concrete));
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

    private static void RequireEfParticipants(IServiceCollection services)
    {
        Require("operational state", RuntimeOperationalStateStoreBackend.Find(services)?.Name, RuntimeOperationalStateStoreBackend.EntityFramework);
        RuntimeOperationalStateStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("workflow execution", WorkflowExecutionStateStoreBackend.Find(services)?.Name, WorkflowExecutionStateStoreBackend.EntityFramework);
        WorkflowExecutionStateStoreBackend.Find(services)!.EnsureOwnsRegisteredContract(services);
        Require("activity execution and inspections", RuntimeActivityExecutionStoreBackend.Find(services)?.Name, RuntimeActivityExecutionStoreBackend.EntityFramework);
        RuntimeActivityExecutionStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("bookmarks", BookmarkStateStoreBackend.Find(services)?.Name, BookmarkStateStoreBackend.EntityFramework);
        BookmarkStateStoreBackend.Find(services)!.EnsureOwnsRegisteredContract(services);
        BookmarkStateStoreBackend.Find(services)!.EnsureOwnsRegisteredAuxiliaryContracts(services);
        Require("workflow test scopes", WorkflowTestScopeStoreBackend.Find(services)?.Name, WorkflowTestScopeStoreBackend.EntityFramework);
        WorkflowTestScopeStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("alteration jobs", RuntimeWorkflowAlterationStoreBackend.Find(services)?.Name, RuntimeWorkflowAlterationStoreBackend.EntityFramework);
        RuntimeWorkflowAlterationStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("executable root leases", RuntimeArtifactStoreBackend.Find(services)?.Name, RuntimeArtifactStoreBackend.EntityFramework);
        RuntimeArtifactStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("scheduler work queue", SchedulerWorkQueueStoreBackend.Find(services)?.Name, SchedulerWorkQueueStoreBackend.EntityFramework);
        SchedulerWorkQueueStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("durable timers", DurableTimerStoreBackend.Find(services)?.Name, DurableTimerStoreBackend.EntityFramework);
        DurableTimerStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("workflow dispatch", RuntimeWorkflowDispatchStoreBackend.Find(services)?.Name, RuntimeWorkflowDispatchStoreBackend.EntityFramework);
        RuntimeWorkflowDispatchStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
        Require("post-commit outbox", RuntimePostCommitOutboxStoreBackend.Find(services)?.Name, RuntimePostCommitOutboxStoreBackend.EntityFramework);
        RuntimePostCommitOutboxStoreBackend.Find(services)!.EnsureOwnsRegisteredContracts(services);
    }

    private static void Require(string participant, string? selected, string expected)
    {
        if (selected != expected)
            throw new InvalidOperationException($"Runtime checkpoint EF persistence requires EF-owned {participant} before replacing the checkpoint contract.");
    }
}
