using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the R21 dispatch adapter for explicit EF composition.</summary>
public static class RuntimeWorkflowDispatchEntityFrameworkCoreRegistration
{
    /// <summary>Replaces the Runtime-owned in-memory dispatch family with its shared-context EF implementation.</summary>
    public static IServiceCollection AddRuntimeWorkflowDispatchEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        try
        {
            var existing = RuntimeWorkflowDispatchStoreBackend.Find(services);
            if (existing?.Name == RuntimeWorkflowDispatchStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime workflow-dispatch EF persistence");
                return services;
            }

            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                throw new InvalidOperationException("Runtime workflow-dispatch EF persistence refuses to replace a selected non-EF backend.");
            }

            var existingContracts = RuntimeWorkflowDispatchStoreBackend.CaptureContractRegistrations(services);
            RuntimeWorkflowDispatchStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(services, existingContracts);
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime workflow-dispatch EF persistence");

            foreach (var descriptor in existingContracts)
                services.Remove(descriptor);

            services.AddScoped<EfWorkflowDispatchStore>();
            var concrete = services.Last();
            var contracts = new[]
            {
                ServiceDescriptor.Scoped<IWorkflowDispatchStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchQueryStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchDeleteStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchRetentionRootStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchAdmissionStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>()),
                ServiceDescriptor.Scoped<IWorkflowDispatchCancellationStore>(provider => provider.GetRequiredService<EfWorkflowDispatchStore>())
            };
            foreach (var descriptor in contracts)
                services.Add(descriptor);

            RuntimeWorkflowDispatchStoreBackend.Register(
                services,
                new(RuntimeWorkflowDispatchStoreBackend.EntityFramework, [concrete, .. contracts]));

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
