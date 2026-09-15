using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core recurring-start schedule store (R27).</summary>
public static class RuntimeRecurringTriggerScheduleEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeRecurringTriggerScheduleEntityFrameworkCore(this IServiceCollection services)
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
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime recurring-trigger schedule EF persistence");
            var existing = RecurringTriggerScheduleStoreBackend.Find(services);
            if (existing?.Name == RecurringTriggerScheduleStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                return services;
            }

            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContract(services);
                if (existing.Name != RecurringTriggerScheduleStoreBackend.Groundwork && existing.Name != RecurringTriggerScheduleStoreBackend.InMemory)
                    throw new InvalidOperationException("Runtime recurring-trigger schedule EF persistence refuses to replace a selected non-EF backend.");
            }
            else
            {
                RecurringTriggerScheduleStoreBackend.EnsureNoUnownedRegistrations(services);
            }

            var removeExisting = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in RecurringTriggerScheduleStoreBackend.CaptureDefaultRegistrations(services).ToArray())
                services.Remove(descriptor);

            services.AddScoped<EfRecurringTriggerScheduleStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IRecurringTriggerScheduleStore>(provider => provider.GetRequiredService<EfRecurringTriggerScheduleStore>());
            services.Add(contract);
            RecurringTriggerScheduleStoreBackend.Register(services, new(RecurringTriggerScheduleStoreBackend.EntityFramework, contract, concrete));
            removeExisting?.Invoke(services);
            RuntimeSharedProjectionStateTransition.Find(services)?.WithdrawIfBothEfSelected(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            foreach (var registrationSnapshot in registrationSnapshots)
                registrationSnapshot.Rollback();
            throw;
        }
    }

    public static IServiceCollection AddRuntimeRecurringTriggerSchedulesEntityFrameworkCore(this IServiceCollection services) =>
        services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore();

    public static IServiceCollection AddRecurringTriggerScheduleEntityFrameworkCore(this IServiceCollection services) =>
        services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore();
}
