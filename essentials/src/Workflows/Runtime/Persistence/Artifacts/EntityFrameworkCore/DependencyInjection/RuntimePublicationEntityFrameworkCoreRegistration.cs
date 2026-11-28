using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class RuntimePublicationEntityFrameworkCoreRegistration
{
    internal static void UseEntityFramework(IServiceCollection services)
    {
        if (RuntimeArtifactStoreBackend.Find(services)?.Name != RuntimeArtifactStoreBackend.EntityFramework)
            throw new InvalidOperationException("Runtime publication EF persistence requires the Runtime artifact EF provider.");
        RuntimeEfContractBackendRegistration.EnsurePublicationContext(services, "Runtime publications EF persistence");

        var serviceSnapshot = services.ToArray();
        var registrationSnapshots = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>().Select(state => state.CaptureSnapshot()).ToArray();
        try
        {
            // Selecting both projections withdraws the previous shared projection-state unit. The switch moves both
            // projections, the slot and its executable source references in one transaction of the same context.
            services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore();
            services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore();
            services.AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore();
            UseActivationSwitch(services);

            services.TryAddScoped<EfRuntimeKernelScheduleOccurrenceStore>();
            services.TryAddScoped<IRuntimeKernelScheduleOccurrenceStore>(provider => provider.GetRequiredService<EfRuntimeKernelScheduleOccurrenceStore>());
            services.TryAddScoped<IRuntimeKernelScheduleOccurrenceDeliveryStore>(provider => provider.GetRequiredService<EfRuntimeKernelScheduleOccurrenceStore>());
            services.TryAddScoped<IRuntimeKernelScheduleOccurrenceRetirementStore>(provider => provider.GetRequiredService<EfRuntimeKernelScheduleOccurrenceStore>());
            services.TryAddScoped<IRuntimeKernelScheduleRetentionRoots>(provider => provider.GetRequiredService<EfRuntimeKernelScheduleOccurrenceStore>());
            services.TryAddScoped<IRuntimeKernelScheduleControlReader, EfRuntimeKernelScheduleControlStore>();
            services.TryAddScoped<IRuntimeKernelScheduleControlScanStore, EfRuntimeKernelScheduleControlStore>();
            services.TryAddScoped<IRuntimeKernelEventPublicationStore, EfRuntimeKernelEventPublicationStore>();
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

    private static void UseActivationSwitch(IServiceCollection services)
    {
        var switches = services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowActivationSwitch)).ToArray();
        if (switches.Length == 1 && switches[0].ImplementationType == typeof(EfWorkflowActivationSwitch))
            return;
        if (switches.Any(descriptor => descriptor.IsKeyedService ||
            !typeof(IDefaultWorkflowActivationSwitch).IsAssignableFrom(descriptor.ImplementationType)))
            throw new InvalidOperationException("An explicit workflow activation switch registration is already present; Runtime EF persistence refuses to replace it implicitly.");

        foreach (var descriptor in switches)
            services.Remove(descriptor);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IWorkflowActivationSwitch, EfWorkflowActivationSwitch>();
    }
}
