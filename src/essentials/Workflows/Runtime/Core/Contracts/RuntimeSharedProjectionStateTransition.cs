using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Coordinates withdrawal of a persistence-owned projection state shared by two Runtime stores.</summary>
/// <remarks>
/// The original backend owns its catalog-specific withdrawal callback. This coordination does not
/// make Runtime Core or EF depend on that catalog; it runs only after both exact EF markers are selected.
/// </remarks>
public sealed class RuntimeSharedProjectionStateTransition
{
    private readonly Action<IServiceCollection> withdrawSharedUnit;

    public RuntimeSharedProjectionStateTransition(Action<IServiceCollection> withdrawSharedUnit)
    {
        ArgumentNullException.ThrowIfNull(withdrawSharedUnit);
        this.withdrawSharedUnit = withdrawSharedUnit;
    }

    public static RuntimeSharedProjectionStateTransition? Find(IServiceCollection services) =>
        services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimeSharedProjectionStateTransition>()
            .SingleOrDefault();

    public static void Register(IServiceCollection services, RuntimeSharedProjectionStateTransition transition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transition);
        services.AddSingleton(transition);
    }

    public bool WithdrawIfBothEfSelected(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var triggerBinding = WorkflowTriggerBindingStoreBackend.Find(services);
        var recurringSchedule = RecurringTriggerScheduleStoreBackend.Find(services);
        if (triggerBinding?.Name != WorkflowTriggerBindingStoreBackend.EntityFramework ||
            recurringSchedule?.Name != RecurringTriggerScheduleStoreBackend.EntityFramework)
            return false;

        withdrawSharedUnit(services);
        return true;
    }
}
