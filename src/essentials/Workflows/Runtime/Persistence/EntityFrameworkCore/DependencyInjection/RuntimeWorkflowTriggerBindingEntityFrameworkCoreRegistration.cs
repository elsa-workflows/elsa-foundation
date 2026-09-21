using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core workflow trigger-binding index.</summary>
public static class RuntimeWorkflowTriggerBindingEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowTriggerBindingEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();
        try
        {
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime workflow trigger-binding EF persistence");
            var existing = WorkflowTriggerBindingStoreBackend.Find(services);
            if (existing?.Name == WorkflowTriggerBindingStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                return services;
            }

            var contractRegistrations = services.Where(x => x.ServiceType == typeof(IWorkflowTriggerBindingStore)).ToArray();
            var defaultRegistrations = contractRegistrations.Where(IsRuntimeDefault).ToArray();
            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContract(services);
                throw new InvalidOperationException("Runtime trigger-binding EF persistence refuses to replace a selected non-EF backend.");
            }
            else if (contractRegistrations.Length > 0 &&
                     (defaultRegistrations.Length != 1 || defaultRegistrations.Length != contractRegistrations.Length))
            {
                throw new InvalidOperationException("An explicit workflow trigger-binding store registration is already present; EF persistence refuses to replace it implicitly.");
            }
            foreach (var descriptor in defaultRegistrations)
                services.Remove(descriptor);

            services.AddScoped<EfWorkflowTriggerBindingStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IWorkflowTriggerBindingStore>(p => p.GetRequiredService<EfWorkflowTriggerBindingStore>());
            services.Add(contract);
            WorkflowTriggerBindingStoreBackend.Register(services, new(WorkflowTriggerBindingStoreBackend.EntityFramework, contract, concrete));
            RuntimeSharedProjectionStateTransition.Find(services)?.WithdrawIfBothEfSelected(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot) services.Add(descriptor);
            foreach (var registrationSnapshot in registrationSnapshots) registrationSnapshot.Rollback();
            throw;
        }
    }

    private static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.Lifetime == ServiceLifetime.Singleton &&
        descriptor.ImplementationType == typeof(InMemoryWorkflowTriggerBindingStore);
}
