using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the R20 EF post-commit outbox adapter for explicit EF composition.</summary>
public static class RuntimePostCommitOutboxEntityFrameworkCoreRegistration
{
    /// <summary>Replaces the Runtime-owned in-memory outbox family with its shared-context EF implementation.</summary>
    public static IServiceCollection AddRuntimePostCommitOutboxEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        try
        {
            var dispatch = RuntimeWorkflowDispatchStoreBackend.Find(services);
            if (dispatch?.Name != RuntimeWorkflowDispatchStoreBackend.EntityFramework)
                throw new InvalidOperationException(
                    "Runtime post-commit outbox EF persistence requires the EF workflow-dispatch backend so dispatch projection and redrive cannot be split across providers.");
            dispatch.EnsureOwnsRegisteredContracts(services);

            var existing = RuntimePostCommitOutboxStoreBackend.Find(services);
            if (existing?.Name == RuntimePostCommitOutboxStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime post-commit outbox EF persistence");
                return services;
            }

            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                if (existing.Name != RuntimePostCommitOutboxStoreBackend.Groundwork)
                    throw new InvalidOperationException("Runtime post-commit outbox EF persistence refuses to replace a selected non-EF backend.");
            }

            RuntimeEfCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "post-commit outbox");

            var removeGroundwork = existing?.Name == RuntimePostCommitOutboxStoreBackend.Groundwork
                ? existing.PrepareRemoveOwnedArtifacts(services)
                : null;
            var existingContracts = existing is null
                ? RuntimePostCommitOutboxStoreBackend.CaptureContractRegistrations(services)
                : [];
            RuntimePostCommitOutboxStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(services, existingContracts);
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime post-commit outbox EF persistence");

            foreach (var descriptor in existingContracts)
                services.Remove(descriptor);

            services.AddScoped<EfRuntimePostCommitOutboxStore>();
            var concrete = services.Last();
            var contracts = new[]
            {
                ServiceDescriptor.Scoped<IRuntimePostCommitOutboxStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStore>()),
                ServiceDescriptor.Scoped<IPostCommitOutboxLookupStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStore>()),
                ServiceDescriptor.Scoped<IRuntimePostCommitOutboxClaimStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStore>()),
                ServiceDescriptor.Scoped<IRuntimePostCommitOutboxClaimCompletionStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchRedriveStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStore>())
            };
            foreach (var descriptor in contracts)
                services.Add(descriptor);

            var evidence = EfRuntimeInfrastructureDurabilityEvidenceRegistration.AddOwned(
                services, WorkflowDispatchDurabilityComponents.Outbox);

            RuntimePostCommitOutboxStoreBackend.Register(
                services,
                new(RuntimePostCommitOutboxStoreBackend.EntityFramework, [concrete, .. contracts, evidence]));

            removeGroundwork?.Invoke(services);

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
}
